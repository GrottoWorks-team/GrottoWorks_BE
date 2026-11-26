using BuildingBlocks.Web.Exceptions;
using Microsoft.EntityFrameworkCore;
using ParishCoordination.Application.Parishes;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Infrastructure.Data;
using ParishCoordination.Infrastructure.Repositories;

namespace ParishCoordination.UnitTests;

public sealed class ParishMapAttachmentTests
{
    [Fact]
    public async Task Recovery_releases_a_reservation_when_the_map_did_not_commit()
    {
        await using var fixture = new Fixture();
        fixture.Files.AfterReserve = async () =>
        {
            // An unrelated update commits on another context and invalidates the tracked parish version.
            await using var other = fixture.Open();
            var parish = await other.Parishes.SingleAsync();
            parish.SetMapImage(Guid.NewGuid(), "other.png", DateTimeOffset.UtcNow);
            await other.SaveChangesAsync();
        };

        await Assert.ThrowsAsync<ParishConcurrencyException>(() => fixture.SaveAsync());
        var intent = await fixture.GetIntentAsync();
        Assert.False(intent.MapSaved);
        Assert.Equal(new[] { "reserve" }, fixture.Files.Commands);

        await fixture.RecoverAsync(intent);
        Assert.Equal(new[] { "reserve", "release" }, fixture.Files.Commands);
        Assert.Empty(await fixture.ListIntentsAsync());
    }

    [Fact]
    public async Task Retry_reuses_the_committed_reservation_without_changing_the_map_version()
    {
        await using var fixture = new Fixture();
        fixture.Files.LoseFinalizeResponse = true;
        var error = await Assert.ThrowsAsync<DomainException>(() => fixture.SaveAsync());
        Assert.Equal("FILE_ATTACHMENT_PENDING", error.Code);
        Assert.Equal(503, error.StatusCode);
        var intent = await fixture.GetIntentAsync();
        Assert.True(intent.MapSaved);
        Assert.Equal(fixture.Image, (await fixture.ReadParishAsync()).MapImageFileId);
        var version = (await fixture.ReadParishAsync()).Version;

        var result = await fixture.SaveAsync();

        Assert.Equal(fixture.Image, result.ImageFileId);
        Assert.Equal(version, (await fixture.ReadParishAsync()).Version);
        Assert.Equal(new[] { "reserve", "finalize", "finalize" }, fixture.Files.Commands);
        Assert.All(fixture.Files.ReservationIds, id => Assert.Equal(intent.Id, id));
        Assert.Empty(await fixture.ListIntentsAsync());
    }

    [Fact]
    public async Task Simultaneous_retries_share_a_reservation_and_only_save_one_map()
    {
        await using var fixture = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Files.AfterReserve = async () =>
        {
            entered.SetResult();
            await resume.Task;
        };
        var first = fixture.SaveAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = fixture.SaveAsync();
        resume.SetResult();
        var results = await Task.WhenAll(first, second);
        Assert.Equal(results[0], results[1]);
        Assert.Equal(new[] { "reserve", "finalize" }, fixture.Files.Commands);
        Assert.Equal(2, (await fixture.ReadParishAsync()).Version);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly DbContextOptions<ParishCoordinationDbContext> options =
            new DbContextOptionsBuilder<ParishCoordinationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        private readonly SemaphoreSlim gate = new(1);
        private readonly Guid parishId = Guid.NewGuid();
        private readonly FakeCurrentUser user;
        public Guid Image { get; } = Guid.NewGuid();
        public FakeFiles Files { get; }

        public Fixture()
        {
            user = new FakeCurrentUser { IsAuthenticated = true, ParishId = parishId };
            Files = new FakeFiles(Image, user.UserId);
            using var db = Open();
            db.Parishes.Add(Parish.Create(parishId, "Test parish", null, null, DateTimeOffset.UtcNow));
            db.SaveChanges();
        }

        public ParishCoordinationDbContext Open() => new(options);

        public async Task<ParishMapDto> SaveAsync()
        {
            await using var db = Open();
            var store = new Journal(db, gate);
            var recovery = new ParishMapAttachmentRecovery(store, Files);
            var handler = new SaveParishMapCommandHandler(new ParishRepository(db), Files, store, recovery, user, TimeProvider.System);
            return await handler.HandleAsync(new SaveParishMapCommand(parishId, Image, "area.png"), default);
        }

        public async Task RecoverAsync(ParishMapAttachmentIntent intent)
        {
            await using var db = Open();
            await new ParishMapAttachmentRecovery(new Journal(db, gate), Files)
                .RecoverAsync(intent.ParishId, intent.FileId, intent.Id, default);
        }

        public async Task<ParishMapAttachmentIntent> GetIntentAsync() => Assert.Single(await ListIntentsAsync());
        public async Task<List<ParishMapAttachmentIntent>> ListIntentsAsync()
        {
            await using var db = Open();
            return await db.Set<ParishMapAttachmentIntent>().ToListAsync();
        }

        public async Task<Parish> ReadParishAsync()
        {
            await using var db = Open();
            return await db.Parishes.SingleAsync();
        }

        public ValueTask DisposeAsync()
        {
            gate.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Journal(ParishCoordinationDbContext db, SemaphoreSlim gate) : IParishMapAttachmentStore
    {
        public async Task<IAsyncDisposable> LockAsync(Guid parishId, CancellationToken ct)
        {
            await gate.WaitAsync(ct);
            return new Lease(gate);
        }

        public Task<ParishMapAttachmentIntent?> FindAsync(Guid parishId, Guid fileId, CancellationToken ct) =>
            db.Set<ParishMapAttachmentIntent>().SingleOrDefaultAsync(row => row.ParishId == parishId && row.FileId == fileId, ct);
        public void Add(ParishMapAttachmentIntent intent) => db.Add(intent);
        public async Task RemoveAsync(ParishMapAttachmentIntent intent, CancellationToken ct)
        {
            db.Remove(intent);
            await db.SaveChangesAsync(ct);
        }

        private sealed class Lease(SemaphoreSlim semaphore) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                semaphore.Release();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class FakeFiles(Guid image, Guid uploader) : IParishFileAttachmentClient
    {
        private Guid? attachedParish;
        public Func<Task>? AfterReserve { get; set; }
        public bool LoseFinalizeResponse { get; set; }
        public List<string> Commands { get; } = [];
        public List<Guid> ReservationIds { get; } = [];

        public Task<ParishMapFileDto?> GetFileAsync(Guid fileId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ParishMapFileDto?>(new(image, uploader, "MAP_IMAGE", attachedParish.HasValue ? "ATTACHED" : "UPLOADED",
                "area.png", attachedParish.HasValue ? "PARISH" : null, attachedParish));

        public async Task ReserveAsync(Guid fileId, Guid parishId, Guid uploaderUserId, Guid reservationId, CancellationToken ct)
        {
            Commands.Add("reserve");
            ReservationIds.Add(reservationId);
            if (AfterReserve is not null) await AfterReserve();
        }

        public Task FinalizeAsync(Guid fileId, Guid parishId, Guid reservationId, CancellationToken ct)
        {
            Commands.Add("finalize");
            ReservationIds.Add(reservationId);
            attachedParish = parishId;
            if (LoseFinalizeResponse)
            {
                LoseFinalizeResponse = false;
                throw new HttpRequestException("Response lost");
            }
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(Guid fileId, Guid parishId, Guid reservationId, CancellationToken ct)
        {
            Commands.Add("release");
            ReservationIds.Add(reservationId);
            return Task.CompletedTask;
        }
    }
}
