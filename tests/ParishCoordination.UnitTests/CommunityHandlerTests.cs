using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using FluentAssertions;
using ParishCoordination.Application.Communities.Commands.CreateCommunity;
using ParishCoordination.Application.Communities.Commands.UpdateCommunity;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.UnitTests;

public sealed class CommunityHandlerTests
{
    private static readonly Guid ParishNorth = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ParishSouth = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task Parish_user_can_create_community_in_own_parish()
    {
        var parishes = RepositoryWithTwoParishes();
        var communities = new FakeCommunityRepository();
        var handler = new CreateCommunityCommandHandler(
            parishes,
            communities,
            User(GrottoWorksRoles.Parish, ParishNorth));

        var result = await handler.HandleAsync(new CreateCommunityCommand(
            ParishNorth,
            "  Demo North  ",
            "  Description  "));

        result.Outcome.Should().Be(CreateCommunityOutcome.Success);
        result.Community.Should().NotBeNull();
        result.Community!.ParishId.Should().Be(ParishNorth);
        result.Community.Name.Should().Be("Demo North");
        result.Community.Description.Should().Be("  Description  ");
        result.Community.Status.Should().Be(CommunityStatus.Active);
        result.Community.Version.Should().Be(1);
        communities.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Admin_can_create_community_in_any_parish()
    {
        var parishes = RepositoryWithTwoParishes();
        var communities = new FakeCommunityRepository();
        var handler = new CreateCommunityCommandHandler(
            parishes,
            communities,
            User(GrottoWorksRoles.Admin));

        var result = await handler.HandleAsync(new CreateCommunityCommand(
            ParishSouth,
            "South Community",
            null));

        result.Outcome.Should().Be(CreateCommunityOutcome.Success);
        result.Community!.ParishId.Should().Be(ParishSouth);
    }

    [Fact]
    public async Task Parish_user_cannot_create_community_in_another_parish()
    {
        var handler = new CreateCommunityCommandHandler(
            RepositoryWithTwoParishes(),
            new FakeCommunityRepository(),
            User(GrottoWorksRoles.Parish, ParishNorth));

        var action = () => handler.HandleAsync(new CreateCommunityCommand(
            ParishSouth,
            "Forbidden",
            null));

        var exception = await action.Should().ThrowAsync<DomainException>();
        exception.Which.Code.Should().Be("PARISH_ACCESS_DENIED");
        exception.Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Create_returns_not_found_for_unknown_parish()
    {
        var handler = new CreateCommunityCommandHandler(
            RepositoryWithTwoParishes(),
            new FakeCommunityRepository(),
            User(GrottoWorksRoles.Admin));

        var result = await handler.HandleAsync(new CreateCommunityCommand(
            Guid.NewGuid(),
            "Unknown parish community",
            null));

        result.Outcome.Should().Be(CreateCommunityOutcome.ParishNotFound);
        result.Community.Should().BeNull();
    }

    [Fact]
    public async Task Duplicate_name_is_rejected_only_inside_the_same_parish()
    {
        var parishes = RepositoryWithTwoParishes();
        var communities = new FakeCommunityRepository();
        var handler = new CreateCommunityCommandHandler(
            parishes,
            communities,
            User(GrottoWorksRoles.Admin));

        (await handler.HandleAsync(new CreateCommunityCommand(
            ParishNorth,
            "Shared Name",
            null))).Outcome.Should().Be(CreateCommunityOutcome.Success);

        (await handler.HandleAsync(new CreateCommunityCommand(
            ParishNorth,
            "Shared Name",
            null))).Outcome.Should().Be(CreateCommunityOutcome.NameConflict);

        var otherParishResult = await handler.HandleAsync(new CreateCommunityCommand(
            ParishSouth,
            "Shared Name",
            null));

        otherParishResult.Outcome.Should().Be(CreateCommunityOutcome.Success);
        communities.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Parish_user_can_update_community_in_own_parish()
    {
        var community = Community.Create(
            Guid.NewGuid(),
            ParishNorth,
            "Old name",
            "Old description",
            DateTimeOffset.UtcNow);
        var communities = new FakeCommunityRepository();
        communities.Items.Add(community);
        var handler = new UpdateCommunityCommandHandler(
            communities,
            User(GrottoWorksRoles.Parish, ParishNorth));

        var result = await handler.HandleAsync(new UpdateCommunityCommand(
            community.Id,
            "  New name  ",
            false,
            null,
            CommunityStatus.Inactive,
            1));

        result.Outcome.Should().Be(UpdateCommunityOutcome.Success);
        result.Community!.Name.Should().Be("New name");
        result.Community.Description.Should().Be("Old description");
        result.Community.Status.Should().Be(CommunityStatus.Inactive);
        result.Community.Version.Should().Be(2);
        result.Community.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Admin_can_update_community_in_any_parish()
    {
        var community = Community.Create(
            Guid.NewGuid(),
            ParishSouth,
            "South community",
            "Description",
            DateTimeOffset.UtcNow);
        var communities = new FakeCommunityRepository();
        communities.Items.Add(community);
        var handler = new UpdateCommunityCommandHandler(
            communities,
            User(GrottoWorksRoles.Admin));

        var result = await handler.HandleAsync(new UpdateCommunityCommand(
            community.Id,
            null,
            true,
            null,
            null,
            1));

        result.Outcome.Should().Be(UpdateCommunityOutcome.Success);
        result.Community!.Description.Should().BeNull();
    }

    [Fact]
    public async Task Parish_user_cannot_update_community_in_another_parish()
    {
        var community = Community.Create(
            Guid.NewGuid(),
            ParishSouth,
            "South community",
            null,
            DateTimeOffset.UtcNow);
        var communities = new FakeCommunityRepository();
        communities.Items.Add(community);
        var handler = new UpdateCommunityCommandHandler(
            communities,
            User(GrottoWorksRoles.Parish, ParishNorth));

        var action = () => handler.HandleAsync(new UpdateCommunityCommand(
            community.Id,
            "Forbidden",
            false,
            null,
            null,
            1));

        var exception = await action.Should().ThrowAsync<DomainException>();
        exception.Which.Code.Should().Be("PARISH_ACCESS_DENIED");
        exception.Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Update_returns_not_found_for_unknown_community()
    {
        var handler = new UpdateCommunityCommandHandler(
            new FakeCommunityRepository(),
            User(GrottoWorksRoles.Admin));

        var result = await handler.HandleAsync(new UpdateCommunityCommand(
            Guid.NewGuid(),
            "Unknown",
            false,
            null,
            null,
            1));

        result.Outcome.Should().Be(UpdateCommunityOutcome.NotFound);
        result.Community.Should().BeNull();
    }

    [Fact]
    public async Task Update_rejects_duplicate_name_in_same_parish()
    {
        var first = Community.Create(
            Guid.NewGuid(),
            ParishNorth,
            "First",
            null,
            DateTimeOffset.UtcNow);
        var second = Community.Create(
            Guid.NewGuid(),
            ParishNorth,
            "Second",
            null,
            DateTimeOffset.UtcNow);
        var communities = new FakeCommunityRepository();
        communities.Items.AddRange([first, second]);
        var handler = new UpdateCommunityCommandHandler(
            communities,
            User(GrottoWorksRoles.Parish, ParishNorth));

        var result = await handler.HandleAsync(new UpdateCommunityCommand(
            first.Id,
            second.Name,
            false,
            null,
            null,
            1));

        result.Outcome.Should().Be(UpdateCommunityOutcome.NameConflict);
        first.Name.Should().Be("First");
        first.Version.Should().Be(1);
    }

    [Fact]
    public async Task Update_rejects_stale_expected_version()
    {
        var community = Community.Create(
            Guid.NewGuid(),
            ParishNorth,
            "Community",
            null,
            DateTimeOffset.UtcNow);
        var communities = new FakeCommunityRepository();
        communities.Items.Add(community);
        var handler = new UpdateCommunityCommandHandler(
            communities,
            User(GrottoWorksRoles.Parish, ParishNorth));

        var result = await handler.HandleAsync(new UpdateCommunityCommand(
            community.Id,
            "Changed",
            false,
            null,
            null,
            99));

        result.Outcome.Should().Be(UpdateCommunityOutcome.VersionMismatch);
        community.Name.Should().Be("Community");
        community.Version.Should().Be(1);
    }

    [Fact]
    public async Task Update_maps_database_concurrency_to_version_mismatch()
    {
        var community = Community.Create(
            Guid.NewGuid(),
            ParishNorth,
            "Community",
            null,
            DateTimeOffset.UtcNow);
        var communities = new FakeCommunityRepository
        {
            ThrowConcurrencyOnSave = true
        };
        communities.Items.Add(community);
        var handler = new UpdateCommunityCommandHandler(
            communities,
            User(GrottoWorksRoles.Parish, ParishNorth));

        var result = await handler.HandleAsync(new UpdateCommunityCommand(
            community.Id,
            "Changed",
            false,
            null,
            null,
            1));

        result.Outcome.Should().Be(UpdateCommunityOutcome.VersionMismatch);
    }

    [Fact]
    public async Task Update_maps_database_unique_race_to_name_conflict()
    {
        var community = Community.Create(
            Guid.NewGuid(),
            ParishNorth,
            "Community",
            null,
            DateTimeOffset.UtcNow);
        var communities = new FakeCommunityRepository
        {
            ThrowNameConflictOnSave = true
        };
        communities.Items.Add(community);
        var handler = new UpdateCommunityCommandHandler(
            communities,
            User(GrottoWorksRoles.Parish, ParishNorth));

        var result = await handler.HandleAsync(new UpdateCommunityCommand(
            community.Id,
            "Changed",
            false,
            null,
            null,
            1));

        result.Outcome.Should().Be(UpdateCommunityOutcome.NameConflict);
    }

    private static FakeCurrentUser User(string role, Guid? parishId = null) =>
        new()
        {
            IsAuthenticated = true,
            Role = role,
            ParishId = parishId
        };

    private static FakeParishRepository RepositoryWithTwoParishes()
    {
        var repository = new FakeParishRepository();
        repository.Items.Add(Parish.Create(
            ParishNorth,
            "North",
            "North address",
            "North description",
            DateTimeOffset.UtcNow));
        repository.Items.Add(Parish.Create(
            ParishSouth,
            "South",
            "South address",
            "South description",
            DateTimeOffset.UtcNow));
        return repository;
    }
}
