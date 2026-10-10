using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using FluentAssertions;
using ParishCoordination.Application.Communities.Commands.CreateCommunity;
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
