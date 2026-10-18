using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Resource.Api.Common;
using Resource.Domain;
using Resource.Domain.Catalog;

namespace Resource.UnitTests.Api;

/// <summary>API Contract 4.4 query rules shared by every Resource list endpoint.</summary>
public sealed class QueryReaderTests
{
    private static IQueryCollection Query(params (string Key, string Value)[] pairs) =>
        new QueryCollection(pairs.GroupBy(p => p.Key).ToDictionary(g => g.Key, g => new StringValues(g.Select(p => p.Value).ToArray())));

    private static DomainRuleException Fails(Action act) => act.Should().Throw<DomainRuleException>().Which;

    [Fact]
    public void Defaults_and_valid_values()
    {
        var reader = new QueryReader(Query(("status", "inactive"), ("sort", "CODE,desc")), "status", "sort", "page", "size");

        reader.Paging().Should().Be((1, 20));
        reader.Sort("name", "name", "code").Should().Be(("code", true));
        reader.Enum<CatalogStatus>("status").Should().Be(CatalogStatus.Inactive);
    }

    [Theory]
    [InlineData("size", "101", "INVALID_PAGE_SIZE")]
    [InlineData("page", "0", "INVALID_PAGE")]
    [InlineData("sort", "price", "INVALID_SORT_FIELD")]
    [InlineData("sort", "name,sideways", "INVALID_SORT_DIRECTION")]
    [InlineData("status", "DELETED", "INVALID_FILTER_VALUE")]
    public void Invalid_values_are_400(string key, string value, string code)
    {
        var reader = new QueryReader(Query((key, value)), "page", "size", "sort", "status");

        var error = Fails(() =>
        {
            reader.Paging();
            reader.Sort("name", "name", "code");
            reader.Enum<CatalogStatus>("status");
        });

        error.Code.Should().Be(code);
        error.Kind.Should().Be(DomainErrorKind.BadRequest);
    }

    [Fact]
    public void Unknown_parameter_is_rejected()
    {
        Fails(() => new QueryReader(Query(("color", "red")), "page")).Code.Should().Be("INVALID_FILTER_FIELD");
    }
}
