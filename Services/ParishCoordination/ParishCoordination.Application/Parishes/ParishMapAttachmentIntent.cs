namespace ParishCoordination.Application.Parishes;

public sealed class ParishMapAttachmentIntent
{
    public Guid Id { get; init; }
    public Guid ParishId { get; init; }
    public Guid FileId { get; init; }
    public DateTimeOffset NextAttemptAt { get; init; }
    public bool MapSaved { get; set; }
}

// The store shares the unit of work with the parish repository.
public interface IParishMapAttachmentStore
{
    Task<IAsyncDisposable> LockAsync(Guid parishId, CancellationToken ct);
    Task<ParishMapAttachmentIntent?> FindAsync(Guid parishId, Guid fileId, CancellationToken ct);
    void Add(ParishMapAttachmentIntent intent);
    Task RemoveAsync(ParishMapAttachmentIntent intent, CancellationToken ct);
}

public sealed class ParishMapAttachmentRecovery(
    IParishMapAttachmentStore intents, IParishFileAttachmentClient files)
{
    public async Task RecoverAsync(Guid parishId, Guid fileId, Guid expectedId, CancellationToken ct)
    {
        await using var lease = await intents.LockAsync(parishId, ct);
        var intent = await intents.FindAsync(parishId, fileId, ct);
        if (intent is null || intent.Id != expectedId)
            return;

        if (intent.MapSaved)
            await FinalizeAsync(intent, ct);
        else
        {
            await files.ReleaseAsync(fileId, parishId, intent.Id, ct);
            await intents.RemoveAsync(intent, ct);
        }
    }

    internal async Task FinalizeAsync(ParishMapAttachmentIntent intent, CancellationToken ct)
    {
        await files.FinalizeAsync(intent.FileId, intent.ParishId, intent.Id, ct);
        await intents.RemoveAsync(intent, ct);
    }
}
