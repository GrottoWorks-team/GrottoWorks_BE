using FluentAssertions;
using Identity.Api.Common;
using Identity.Application.Accounts;
using Identity.Domain;
using Identity.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Identity.UnitTests.Api;

/// <summary>API Contract 4.4: paging defaults, size cap, sort syntax, unknown fields → 400.</summary>
public sealed class UserListQueryParserTests
{
    private static IQueryCollection Query(params (string Key, string Value)[] pairs) =>
        new QueryCollection(pairs
            .GroupBy(pair => pair.Key)
            .ToDictionary(group => group.Key, group => new StringValues(group.Select(pair => pair.Value).ToArray())));

    private static string CodeOf(Action act) => act.Should().Throw<DomainException>().Which.Code;

    [Fact]
    public void Defaults_to_page_1_size_20_sorted_by_display_name()
    {
        var criteria = UserListQueryParser.Parse(Query(), allowParishFilter: false);

        criteria.Page.Should().Be(1);
        criteria.Size.Should().Be(20);
        criteria.SortField.Should().Be(UserSortField.DisplayName);
        criteria.SortDescending.Should().BeFalse();
    }

    [Fact]
    public void Parses_filters_and_sort()
    {
        var communityId = Guid.NewGuid();
        var criteria = UserListQueryParser.Parse(
            Query(("page", "2"), ("size", "50"), ("sort", "createdAt,desc"), ("role", "leader"),
                ("status", "locked"), ("communityId", communityId.ToString()), ("q", " an ")),
            allowParishFilter: false);

        criteria.Should().BeEquivalentTo(new UserSearchCriteria(
            2, 50, null, RoleCodes.Leader, UserStatus.Locked, communityId, null, "an", UserSortField.CreatedAt, true));
    }

    [Theory]
    [InlineData("size", "101", "INVALID_PAGE_SIZE")]
    [InlineData("size", "0", "INVALID_PAGE_SIZE")]
    [InlineData("page", "0", "INVALID_PAGE")]
    [InlineData("page", "abc", "INVALID_PAGE")]
    [InlineData("sort", "password,asc", "INVALID_SORT_FIELD")]
    [InlineData("sort", "email,up", "INVALID_SORT_DIRECTION")]
    [InlineData("role", "ROOT", "INVALID_FILTER_VALUE")]
    [InlineData("status", "DELETED", "INVALID_FILTER_VALUE")]
    [InlineData("skillId", "not-a-uuid", "INVALID_FILTER_VALUE")]
    [InlineData("foo", "bar", "INVALID_FILTER_FIELD")]
    public void Rejects_invalid_parameters(string key, string value, string expectedCode)
    {
        CodeOf(() => UserListQueryParser.Parse(Query((key, value)), allowParishFilter: false))
            .Should().Be(expectedCode);
    }

    [Fact]
    public void ParishId_filter_is_only_accepted_for_admin_listing()
    {
        var parishId = Guid.NewGuid();

        CodeOf(() => UserListQueryParser.Parse(Query(("parishId", parishId.ToString())), allowParishFilter: false))
            .Should().Be("INVALID_FILTER_FIELD");

        UserListQueryParser.Parse(Query(("parishId", parishId.ToString())), allowParishFilter: true)
            .ParishId.Should().Be(parishId);
    }
}
