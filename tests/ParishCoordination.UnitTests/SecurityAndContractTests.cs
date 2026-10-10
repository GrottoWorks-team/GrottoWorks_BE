using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using BuildingBlocks.Pagination;
using BuildingBlocks.Security;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Exceptions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using ParishCoordination.Api.Requests;

namespace ParishCoordination.UnitTests;

public sealed class SecurityAndContractTests
{
    [Fact]
    public void Parish_access_allows_admin_and_same_tenant_but_rejects_other_tenant()
    {
        var parishId = Guid.NewGuid();
        var admin = new FakeCurrentUser { IsAuthenticated = true, Role = GrottoWorksRoles.Admin };
        var sameParish = new FakeCurrentUser { IsAuthenticated = true, Role = GrottoWorksRoles.Parish, ParishId = parishId };
        var otherParish = new FakeCurrentUser { IsAuthenticated = true, Role = GrottoWorksRoles.Parish, ParishId = Guid.NewGuid() };

        FluentActions.Invoking(() => ParishAccess.EnsureSameParish(admin, Guid.NewGuid())).Should().NotThrow();
        FluentActions.Invoking(() => ParishAccess.EnsureSameParish(sameParish, parishId)).Should().NotThrow();
        FluentActions.Invoking(() => ParishAccess.EnsureSameParish(otherParish, parishId))
            .Should().Throw<DomainException>().Which.Code.Should().Be("PARISH_ACCESS_DENIED");
    }

    [Fact]
    public void Anonymous_access_is_rejected_with_401_contract()
    {
        var action = () => ParishAccess.EnsureSameParish(
            new FakeCurrentUser { IsAuthenticated = false },
            Guid.NewGuid());

        action.Should().Throw<DomainException>().Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public void Create_and_update_requests_validate_required_and_partial_fields()
    {
        var invalidCreate = new CreateParishRequest();
        var invalidCommunity = new CreateCommunityRequest();
        var tooLongCommunity = new CreateCommunityRequest { Name = new string('x', 151) };
        var whitespaceCommunity = new CreateCommunityRequest { Name = "   " };
        var invalidUpdate = new UpdateParishRequest();
        var validUpdate = new UpdateParishRequest { Description = "updated" };

        Validate(invalidCreate).Should().Contain(result => result.MemberNames.Contains(nameof(CreateParishRequest.Name)));
        Validate(invalidCommunity).Should().Contain(result => result.MemberNames.Contains(nameof(CreateCommunityRequest.Name)));
        Validate(tooLongCommunity).Should().Contain(result => result.MemberNames.Contains(nameof(CreateCommunityRequest.Name)));
        Validate(whitespaceCommunity).Should().Contain(result => result.MemberNames.Contains(nameof(CreateCommunityRequest.Name)));
        Validate(invalidUpdate).Should().Contain(result => result.ErrorMessage!.Contains("At least one field"));
        Validate(validUpdate).Should().BeEmpty();
    }

    [Fact]
    public void Pagination_defaults_sorts_and_enforces_page_size_limit()
    {
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            ["page"] = "2",
            ["size"] = "25",
            ["sort"] = "name,DESC"
        });

        var parsed = PaginationRequest.FromQuery(query, "name");

        parsed.Page.Should().Be(2);
        parsed.Size.Should().Be(25);
        parsed.Sort.Should().Be("name,desc");

        var invalid = new QueryCollection(new Dictionary<string, StringValues> { ["size"] = "101" });
        FluentActions.Invoking(() => PaginationRequest.FromQuery(invalid, "name"))
            .Should().Throw<DomainException>().Which.Code.Should().Be("PAGE_SIZE_EXCEEDED");
    }

    [Fact]
    public void Success_response_preserves_envelope_and_correlation_id()
    {
        var context = new DefaultHttpContext();
        context.Items[CorrelationIdMiddleware.ItemKey] = "corr-123";

        var response = ApiResults.Ok(new { Value = 42 }, context);

        response.Data.Value.Should().Be(42);
        response.Meta.CorrelationId.Should().Be("corr-123");
        JsonSerializer.Serialize(response).Should().Contain("corr-123");
    }

    private static List<ValidationResult> Validate(object instance)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true);
        return results;
    }
}
