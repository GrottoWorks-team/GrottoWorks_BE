using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using FluentAssertions;
using ParishCoordination.Application.Communities;
using ParishCoordination.Application.Communities.Queries.GetCommunities;
using ParishCoordination.Application.Parishes;
using ParishCoordination.Application.Parishes.Commands.CreateParish;
using ParishCoordination.Application.Parishes.Commands.UpdateParish;
using ParishCoordination.Application.Parishes.Queries.GetParish;
using ParishCoordination.Application.Parishes.Queries.GetParishes;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.UnitTests;

public sealed class ParishHandlerTests
{
    private static readonly Guid ParishNorth = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ParishSouth = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task Create_adds_active_parish()
    {
        var repository = new FakeParishRepository();
        var handler = new CreateParishCommandHandler(repository);

        var result = await handler.HandleAsync(new CreateParishCommand("North", "Address", "Description"));

        result.Name.Should().Be("North");
        result.Status.Should().Be(ParishStatus.Active);
        result.Version.Should().Be(1);
        repository.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Admin_can_list_all_parishes_and_sort_descending()
    {
        var repository = RepositoryWithTwoParishes();
        var handler = new GetParishesQueryHandler(repository, new FakeCurrentUser
        {
            IsAuthenticated = true,
            Role = GrottoWorksRoles.Admin
        });

        var result = await handler.HandleAsync(new GetParishesQuery(1, 20, "name,desc"));

        result.TotalItems.Should().Be(2);
        result.Items.Select(item => item.Name).Should().Equal("South", "North");
        repository.LastScopeId.Should().BeNull();
    }

    [Fact]
    public async Task Parish_user_can_list_only_own_parish()
    {
        var repository = RepositoryWithTwoParishes();
        var handler = new GetParishesQueryHandler(repository, new FakeCurrentUser
        {
            IsAuthenticated = true,
            Role = GrottoWorksRoles.Parish,
            ParishId = ParishNorth
        });

        var result = await handler.HandleAsync(new GetParishesQuery());

        result.TotalItems.Should().Be(1);
        result.Items.Should().ContainSingle(item => item.Id == ParishNorth);
        repository.LastScopeId.Should().Be(ParishNorth);
    }

    [Fact]
    public async Task Anonymous_list_is_rejected()
    {
        var handler = new GetParishesQueryHandler(
            RepositoryWithTwoParishes(),
            new FakeCurrentUser { IsAuthenticated = false });

        var action = () => handler.HandleAsync(new GetParishesQuery());

        var exception = await action.Should().ThrowAsync<DomainException>();
        exception.Which.Code.Should().Be("UNAUTHENTICATED");
        exception.Which.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Get_and_update_other_parish_are_forbidden()
    {
        var repository = RepositoryWithTwoParishes();
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            Role = GrottoWorksRoles.Parish,
            ParishId = ParishNorth
        };

        var getAction = () => new GetParishQueryHandler(repository, user)
            .HandleAsync(new GetParishQuery(ParishSouth));
        var updateAction = () => new UpdateParishCommandHandler(repository, user)
            .HandleAsync(new UpdateParishCommand(ParishSouth, "Changed", false, null, false, null, null, 1));

        (await getAction.Should().ThrowAsync<DomainException>()).Which.StatusCode.Should().Be(403);
        (await updateAction.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("PARISH_ACCESS_DENIED");
    }

    [Fact]
    public async Task Update_partial_request_keeps_unsent_fields_and_increments_version()
    {
        var repository = RepositoryWithTwoParishes();
        var handler = new UpdateParishCommandHandler(repository, new FakeCurrentUser
        {
            IsAuthenticated = true,
            Role = GrottoWorksRoles.Parish,
            ParishId = ParishNorth
        });

        var result = await handler.HandleAsync(new UpdateParishCommand(
            ParishNorth,
            "North Updated",
            false,
            null,
            true,
            "New description",
            null,
            1));

        result.Outcome.Should().Be(UpdateParishOutcome.Success);
        result.Parish!.Name.Should().Be("North Updated");
        result.Parish.Address.Should().Be("North address");
        result.Parish.Description.Should().Be("New description");
        result.Parish.Version.Should().Be(2);
    }

    [Fact]
    public async Task Update_returns_version_mismatch_for_stale_version_or_concurrency_conflict()
    {
        var repository = RepositoryWithTwoParishes();
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            Role = GrottoWorksRoles.Parish,
            ParishId = ParishNorth
        };
        var handler = new UpdateParishCommandHandler(repository, user);

        var stale = await handler.HandleAsync(new UpdateParishCommand(
            ParishNorth, "Changed", false, null, false, null, null, 99));
        repository.ThrowConcurrencyOnSave = true;
        var concurrent = await handler.HandleAsync(new UpdateParishCommand(
            ParishNorth, "Changed", false, null, false, null, null, 1));

        stale.Outcome.Should().Be(UpdateParishOutcome.VersionMismatch);
        concurrent.Outcome.Should().Be(UpdateParishOutcome.VersionMismatch);
    }

    [Fact]
    public async Task Get_and_community_list_return_not_found_for_unknown_parish()
    {
        var repository = new FakeParishRepository();
        var user = new FakeCurrentUser { IsAuthenticated = true, Role = GrottoWorksRoles.Admin };

        (await new GetParishQueryHandler(repository, user)
            .HandleAsync(new GetParishQuery(Guid.NewGuid()))).Should().BeNull();
        (await new GetCommunitiesQueryHandler(repository, new FakeCommunityRepository(), user)
            .HandleAsync(new GetCommunitiesQuery(Guid.NewGuid()))).Should().BeNull();
    }

    [Fact]
    public async Task Community_list_is_tenant_scoped_and_sorted()
    {
        var repository = RepositoryWithTwoParishes();
        var communities = new FakeCommunityRepository();
        communities.Items.Add(Community.Create(Guid.NewGuid(), ParishNorth, "Zulu", null, DateTimeOffset.UtcNow));
        communities.Items.Add(Community.Create(Guid.NewGuid(), ParishNorth, "Alpha", null, DateTimeOffset.UtcNow));
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            Role = GrottoWorksRoles.Parish,
            ParishId = ParishNorth
        };

        var result = await new GetCommunitiesQueryHandler(repository, communities, user)
            .HandleAsync(new GetCommunitiesQuery(ParishNorth));

        result!.Select(community => community.Name).Should().Equal("Alpha", "Zulu");
    }

    private static FakeParishRepository RepositoryWithTwoParishes()
    {
        var repository = new FakeParishRepository();
        repository.Items.Add(Parish.Create(ParishNorth, "North", "North address", "North description", DateTimeOffset.UtcNow));
        repository.Items.Add(Parish.Create(ParishSouth, "South", "South address", "South description", DateTimeOffset.UtcNow));
        return repository;
    }
}