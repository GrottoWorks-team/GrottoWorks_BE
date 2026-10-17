using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ParishCoordination.Application.WorkAreaCategories;
using ParishCoordination.Application.WorkAreaCategories.Queries.ListWorkAreaCategories;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;
using ParishCoordination.Infrastructure.Data;
using ParishCoordination.Infrastructure.Repositories;

namespace ParishCoordination.UnitTests;

public sealed class WorkAreaCategoryHandlerTests
{
    private static readonly Guid AlphaCategoryId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AlphaCategorySecondId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid BetaCategoryId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task No_status_filter_returns_all_categories_in_stable_name_order()
    {
        await using var context = CreateDbContext();
        context.WorkAreaCategories.AddRange(Categories());
        await context.SaveChangesAsync();
        var handler = new ListWorkAreaCategoriesQueryHandler(
            new WorkAreaCategoryRepository(context));

        var result = await handler.HandleAsync(
            new ListWorkAreaCategoriesQuery(null));

        result.Select(category => category.Id).Should().Equal(
            AlphaCategoryId,
            AlphaCategorySecondId,
            BetaCategoryId);
    }

    [Theory]
    [InlineData(WorkAreaCategoryStatus.Active, 2)]
    [InlineData(WorkAreaCategoryStatus.Inactive, 1)]
    public async Task Status_filter_returns_only_matching_categories(
        WorkAreaCategoryStatus status,
        int expectedCount)
    {
        await using var context = CreateDbContext();
        context.WorkAreaCategories.AddRange(Categories());
        await context.SaveChangesAsync();
        var handler = new ListWorkAreaCategoriesQueryHandler(
            new WorkAreaCategoryRepository(context));

        var result = await handler.HandleAsync(
            new ListWorkAreaCategoriesQuery(status));

        result.Should().HaveCount(expectedCount);
        result.Should().OnlyContain(category => category.Status == status);
    }

    [Fact]
    public async Task No_data_returns_empty_list()
    {
        var handler = new ListWorkAreaCategoriesQueryHandler(
            new FakeWorkAreaCategoryRepository());

        var result = await handler.HandleAsync(
            new ListWorkAreaCategoriesQuery(null));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Dto_maps_all_contract_fields()
    {
        var repository = new FakeWorkAreaCategoryRepository();
        repository.Items.Add(WorkAreaCategory.Create(
            AlphaCategoryId,
            "DECORATION",
            "Decoration",
            "Build and install decorations",
            WorkAreaCategoryStatus.Active));
        var handler = new ListWorkAreaCategoriesQueryHandler(repository);

        var result = await handler.HandleAsync(
            new ListWorkAreaCategoriesQuery(null));

        result.Should().ContainSingle().Which.Should().Be(
            new WorkAreaCategoryDto(
                AlphaCategoryId,
                "DECORATION",
                "Decoration",
                "Build and install decorations",
                WorkAreaCategoryStatus.Active));
    }

    [Fact]
    public void Ef_model_matches_work_area_category_schema()
    {
        var options = new DbContextOptionsBuilder<ParishCoordinationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new ParishCoordinationDbContext(options);
        var entity = context.Model.FindEntityType(typeof(WorkAreaCategory))!;

        entity.GetTableName().Should().Be("work_area_category");
        entity.FindProperty(nameof(WorkAreaCategory.Id))!
            .GetColumnName().Should().Be("work_area_category_id");
        entity.FindProperty(nameof(WorkAreaCategory.Code))!
            .GetColumnName().Should().Be("category_code");
        entity.FindProperty(nameof(WorkAreaCategory.Code))!
            .GetMaxLength().Should().Be(50);
        entity.FindProperty(nameof(WorkAreaCategory.Name))!
            .GetColumnName().Should().Be("category_name");
        entity.FindProperty(nameof(WorkAreaCategory.Name))!
            .GetMaxLength().Should().Be(100);
        entity.FindProperty(nameof(WorkAreaCategory.Description))!
            .GetColumnName().Should().Be("category_description");
        entity.FindProperty(nameof(WorkAreaCategory.Status))!
            .GetColumnName().Should().Be("category_status");
        entity.FindProperty(nameof(WorkAreaCategory.Status))!
            .GetValueConverter().Should().NotBeNull();
        entity.GetIndexes().Should().Contain(index =>
            index.IsUnique &&
            index.Properties.Single().Name == nameof(WorkAreaCategory.Code));
    }

    private static ParishCoordinationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ParishCoordinationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ParishCoordinationDbContext(options);
    }

    private static IReadOnlyList<WorkAreaCategory> Categories()
    {
        return
        [
            WorkAreaCategory.Create(
                BetaCategoryId,
                "LIGHTING",
                "Lighting",
                null,
                WorkAreaCategoryStatus.Active),
            WorkAreaCategory.Create(
                AlphaCategorySecondId,
                "DECORATION_B",
                "Decoration",
                null,
                WorkAreaCategoryStatus.Inactive),
            WorkAreaCategory.Create(
                AlphaCategoryId,
                "DECORATION_A",
                "Decoration",
                null,
                WorkAreaCategoryStatus.Active)
        ];
    }
}
