using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ParishCoordination.Application.WorkAreaCategories;
using ParishCoordination.Application.WorkAreaCategories.Commands.CreateWorkAreaCategory;
using ParishCoordination.Application.WorkAreaCategories.Commands.UpdateWorkAreaCategory;
using ParishCoordination.Application.WorkAreaCategories.Queries.ListWorkAreaCategories;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;
using ParishCoordination.Infrastructure.Data;
using ParishCoordination.Infrastructure.Repositories;
using ParishCoordination.Api.Requests;

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
    public async Task Create_normalizes_fields_and_creates_active_category()
    {
        var repository = new FakeWorkAreaCategoryRepository();
        var handler = new CreateWorkAreaCategoryCommandHandler(repository);

        var result = await handler.HandleAsync(
            new CreateWorkAreaCategoryCommand(
                "  decoration ",
                "  Decoration  ",
                "  Build decorations  "));

        result.Outcome.Should().Be(CreateWorkAreaCategoryOutcome.Success);
        result.Category!.Id.Should().NotBe(Guid.Empty);
        result.Category.Code.Should().Be("DECORATION");
        result.Category.Name.Should().Be("Decoration");
        result.Category.Description.Should().Be("Build decorations");
        result.Category.Status.Should().Be(WorkAreaCategoryStatus.Active);
        repository.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task Create_converts_whitespace_description_to_null()
    {
        var repository = new FakeWorkAreaCategoryRepository();
        var handler = new CreateWorkAreaCategoryCommandHandler(repository);

        var result = await handler.HandleAsync(
            new CreateWorkAreaCategoryCommand(
                "LIGHTING",
                "Lighting",
                "   "));

        result.Category!.Description.Should().BeNull();
    }

    [Fact]
    public async Task Create_rejects_existing_normalized_code_before_saving()
    {
        var repository = new FakeWorkAreaCategoryRepository();
        repository.Items.Add(WorkAreaCategory.Create(
            AlphaCategoryId,
            "DECORATION",
            "Decoration",
            null,
            WorkAreaCategoryStatus.Active));
        var handler = new CreateWorkAreaCategoryCommandHandler(repository);

        var result = await handler.HandleAsync(
            new CreateWorkAreaCategoryCommand(
                " decoration ",
                "Duplicate",
                null));

        result.Outcome.Should().Be(CreateWorkAreaCategoryOutcome.CodeConflict);
        result.Category.Should().BeNull();
        repository.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task Create_maps_unique_constraint_race_to_code_conflict()
    {
        var repository = new FakeWorkAreaCategoryRepository
        {
            ThrowCodeConflictOnSave = true
        };
        var handler = new CreateWorkAreaCategoryCommandHandler(repository);

        var result = await handler.HandleAsync(
            new CreateWorkAreaCategoryCommand(
                "DECORATION",
                "Decoration",
                null));

        result.Outcome.Should().Be(CreateWorkAreaCategoryOutcome.CodeConflict);
        result.Category.Should().BeNull();
        repository.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public void Create_request_validates_required_fields_and_length_limits()
    {
        var missingFields = Validate(new CreateWorkAreaCategoryRequest());
        var whitespaceFields = Validate(new CreateWorkAreaCategoryRequest
        {
            Code = "   ",
            Name = "   "
        });
        var longFields = Validate(new CreateWorkAreaCategoryRequest
        {
            Code = new string('c', 51),
            Name = new string('n', 101)
        });

        missingFields.Should().Contain(result =>
            result.MemberNames.Contains(nameof(CreateWorkAreaCategoryRequest.Code)));
        missingFields.Should().Contain(result =>
            result.MemberNames.Contains(nameof(CreateWorkAreaCategoryRequest.Name)));
        whitespaceFields.Should().Contain(result =>
            result.MemberNames.Contains(nameof(CreateWorkAreaCategoryRequest.Code)));
        whitespaceFields.Should().Contain(result =>
            result.MemberNames.Contains(nameof(CreateWorkAreaCategoryRequest.Name)));
        longFields.Should().Contain(result =>
            result.MemberNames.Contains(nameof(CreateWorkAreaCategoryRequest.Code)));
        longFields.Should().Contain(result =>
            result.MemberNames.Contains(nameof(CreateWorkAreaCategoryRequest.Name)));
    }

    [Fact]
    public async Task Update_changes_fields_and_keeps_code_immutable()
    {
        var repository = CategoryRepository();
        var handler = new UpdateWorkAreaCategoryCommandHandler(repository);

        var result = await handler.HandleAsync(
            new UpdateWorkAreaCategoryCommand(
                AlphaCategoryId,
                "  Updated decoration  ",
                true,
                "Updated description",
                WorkAreaCategoryStatus.Inactive));

        result.Outcome.Should().Be(UpdateWorkAreaCategoryOutcome.Success);
        result.Category!.Code.Should().Be("DECORATION");
        result.Category.Name.Should().Be("Updated decoration");
        result.Category.Description.Should().Be("Updated description");
        result.Category.Status.Should().Be(WorkAreaCategoryStatus.Inactive);
        repository.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task Update_preserves_fields_that_were_not_provided()
    {
        var repository = CategoryRepository();
        var handler = new UpdateWorkAreaCategoryCommandHandler(repository);

        var result = await handler.HandleAsync(
            new UpdateWorkAreaCategoryCommand(
                AlphaCategoryId,
                "New name",
                false,
                null,
                null));

        result.Category!.Name.Should().Be("New name");
        result.Category.Description.Should().Be("Original description");
        result.Category.Status.Should().Be(WorkAreaCategoryStatus.Active);
    }

    [Fact]
    public async Task Update_can_clear_description()
    {
        var repository = CategoryRepository();
        var handler = new UpdateWorkAreaCategoryCommandHandler(repository);

        var result = await handler.HandleAsync(
            new UpdateWorkAreaCategoryCommand(
                AlphaCategoryId,
                null,
                true,
                null,
                null));

        result.Category!.Description.Should().BeNull();
    }

    [Fact]
    public async Task Update_can_deactivate_and_reactivate_category()
    {
        var repository = CategoryRepository();
        var handler = new UpdateWorkAreaCategoryCommandHandler(repository);

        var inactive = await handler.HandleAsync(
            new UpdateWorkAreaCategoryCommand(
                AlphaCategoryId,
                null,
                false,
                null,
                WorkAreaCategoryStatus.Inactive));
        var active = await handler.HandleAsync(
            new UpdateWorkAreaCategoryCommand(
                AlphaCategoryId,
                null,
                false,
                null,
                WorkAreaCategoryStatus.Active));

        inactive.Category!.Status.Should().Be(WorkAreaCategoryStatus.Inactive);
        active.Category!.Status.Should().Be(WorkAreaCategoryStatus.Active);
        repository.SaveChangesCount.Should().Be(2);
    }

    [Fact]
    public async Task Update_returns_not_found_for_unknown_category()
    {
        var handler = new UpdateWorkAreaCategoryCommandHandler(
            new FakeWorkAreaCategoryRepository());

        var result = await handler.HandleAsync(
            new UpdateWorkAreaCategoryCommand(
                Guid.NewGuid(),
                "New name",
                false,
                null,
                null));

        result.Outcome.Should().Be(UpdateWorkAreaCategoryOutcome.NotFound);
        result.Category.Should().BeNull();
    }

    [Fact]
    public void Update_request_rejects_empty_and_invalid_name()
    {
        var emptyRequest = Validate(new UpdateWorkAreaCategoryRequest());
        var whitespaceName = Validate(new UpdateWorkAreaCategoryRequest
        {
            Name = "   "
        });
        var longName = Validate(new UpdateWorkAreaCategoryRequest
        {
            Name = new string('n', 101)
        });

        emptyRequest.Should().Contain(result =>
            result.ErrorMessage!.Contains("At least one field"));
        whitespaceName.Should().Contain(result =>
            result.MemberNames.Contains(nameof(UpdateWorkAreaCategoryRequest.Name)));
        longName.Should().Contain(result =>
            result.MemberNames.Contains(nameof(UpdateWorkAreaCategoryRequest.Name)));
    }

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

    private static FakeWorkAreaCategoryRepository CategoryRepository()
    {
        var repository = new FakeWorkAreaCategoryRepository();
        repository.Items.Add(WorkAreaCategory.Create(
            AlphaCategoryId,
            "DECORATION",
            "Decoration",
            "Original description",
            WorkAreaCategoryStatus.Active));
        return repository;
    }

    private static List<ValidationResult> Validate(object instance)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(
            instance,
            new ValidationContext(instance),
            results,
            validateAllProperties: true);
        return results;
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
