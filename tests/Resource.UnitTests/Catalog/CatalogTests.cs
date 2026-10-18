using Resource.Application.Catalog;
using Resource.Domain;
using Resource.Domain.Catalog;

namespace Resource.UnitTests.Catalog;

/// <summary>F-RES-02: material categories and catalog.</summary>
public sealed class CatalogTests
{
    private readonly FakeCatalogStore _store = new();

    private static async Task<DomainRuleException> Fails(Func<Task> act) =>
        (await act.Should().ThrowAsync<DomainRuleException>()).Which;

    [Fact]
    public async Task CreateCategory_normalizes_code_and_rejects_duplicates()
    {
        var created = await new CreateMaterialCategoryCommandHandler(_store)
            .HandleAsync(new CreateMaterialCategoryCommand(" light ", " Đèn ", "  "));

        created.Code.Should().Be("LIGHT");
        created.Name.Should().Be("Đèn");
        created.Description.Should().BeNull();
        created.Status.Should().Be(CatalogStatus.Active);

        var duplicate = await Fails(() => new CreateMaterialCategoryCommandHandler(_store)
            .HandleAsync(new CreateMaterialCategoryCommand("LIGHT", "Other", null)));
        duplicate.Code.Should().Be("MATERIAL_CATEGORY_CODE_ALREADY_EXISTS");
        duplicate.Kind.Should().Be(DomainErrorKind.Conflict);
    }

    [Theory]
    [InlineData("")]
    [InlineData("HAS SPACE")]
    public void Category_code_must_be_a_single_token(string code)
    {
        var act = () => MaterialCategory.Create(code, "Name", null);

        act.Should().Throw<DomainRuleException>().Which.Kind.Should().Be(DomainErrorKind.Validation);
    }

    [Fact]
    public async Task ListCategories_defaults_to_active()
    {
        _store.SeedCategory("A", "Active one");
        var inactive = _store.SeedCategory("B", "Inactive one");
        inactive.Update(null, null, CatalogStatus.Inactive);

        var active = await new ListMaterialCategoriesQueryHandler(_store).HandleAsync(null);
        var onlyInactive = await new ListMaterialCategoriesQueryHandler(_store).HandleAsync(CatalogStatus.Inactive);

        active.Select(c => c.Code).Should().Equal("A");
        onlyInactive.Select(c => c.Code).Should().Equal("B");
    }

    [Fact]
    public async Task Category_with_active_materials_cannot_be_deactivated()
    {
        var category = _store.SeedCategory();
        var material = _store.SeedMaterial(category);

        (await Fails(() => new UpdateMaterialCategoryCommandHandler(_store)
                .HandleAsync(new UpdateMaterialCategoryCommand(category.MaterialCategoryId, null, null, CatalogStatus.Inactive))))
            .Code.Should().Be("MATERIAL_CATEGORY_HAS_ACTIVE_MATERIALS");

        material.Update(null, null, null, CatalogStatus.Inactive, isReferenced: false);
        var updated = await new UpdateMaterialCategoryCommandHandler(_store)
            .HandleAsync(new UpdateMaterialCategoryCommand(category.MaterialCategoryId, "Renamed", "", CatalogStatus.Inactive));

        updated.Status.Should().Be(CatalogStatus.Inactive);
        updated.Name.Should().Be("Renamed");
        updated.Description.Should().BeNull("an empty string clears the description");
    }

    [Fact]
    public async Task UpdateCategory_unknown_is_not_found()
    {
        (await Fails(() => new UpdateMaterialCategoryCommandHandler(_store)
                .HandleAsync(new UpdateMaterialCategoryCommand(Guid.NewGuid(), "X", null, null))))
            .Kind.Should().Be(DomainErrorKind.NotFound);
    }

    [Fact]
    public async Task CreateMaterial_requires_known_active_category_and_unique_code()
    {
        var category = _store.SeedCategory();
        var handler = new CreateMaterialCommandHandler(_store);

        var created = await handler.HandleAsync(new CreateMaterialCommand(category.MaterialCategoryId, "cable-2x1", "Dây điện 2x1", " m ", null));
        created.Code.Should().Be("CABLE-2X1");
        created.Unit.Should().Be("m");
        created.CategoryId.Should().Be(category.MaterialCategoryId);

        (await Fails(() => handler.HandleAsync(new CreateMaterialCommand(category.MaterialCategoryId, "CABLE-2X1", "Dup", "m", null))))
            .Code.Should().Be("MATERIAL_CODE_ALREADY_EXISTS");
        (await Fails(() => handler.HandleAsync(new CreateMaterialCommand(Guid.NewGuid(), "X1", "X", "m", null))))
            .Code.Should().Be("MATERIAL_CATEGORY_UNKNOWN");

        category.Update(null, null, CatalogStatus.Inactive);
        (await Fails(() => handler.HandleAsync(new CreateMaterialCommand(category.MaterialCategoryId, "X2", "X", "m", null))))
            .Code.Should().Be("MATERIAL_CATEGORY_INACTIVE");
    }

    [Fact]
    public async Task UpdateMaterial_unit_is_locked_once_referenced()
    {
        var category = _store.SeedCategory();
        var material = _store.SeedMaterial(category, unit: "m");
        var handler = new UpdateMaterialCommandHandler(_store);

        var changed = await handler.HandleAsync(new UpdateMaterialCommand(material.MaterialId, null, null, "cuộn", null, null));
        changed.Unit.Should().Be("cuộn");

        _store.ReferencedMaterials.Add(material.MaterialId);
        (await Fails(() => handler.HandleAsync(new UpdateMaterialCommand(material.MaterialId, null, null, "m", null, null))))
            .Code.Should().Be("MATERIAL_UNIT_LOCKED");

        var renamed = await handler.HandleAsync(new UpdateMaterialCommand(material.MaterialId, null, "Đèn LED", "cuộn", null, CatalogStatus.Inactive));
        renamed.Name.Should().Be("Đèn LED");
        renamed.Status.Should().Be(CatalogStatus.Inactive);
    }

    [Fact]
    public async Task UpdateMaterial_moves_only_to_an_active_category()
    {
        var category = _store.SeedCategory();
        var other = _store.SeedCategory("DECOR", "Decor");
        var material = _store.SeedMaterial(category);
        var handler = new UpdateMaterialCommandHandler(_store);

        (await handler.HandleAsync(new UpdateMaterialCommand(material.MaterialId, other.MaterialCategoryId, null, null, null, null)))
            .CategoryId.Should().Be(other.MaterialCategoryId);

        category.Update(null, null, CatalogStatus.Inactive);
        (await Fails(() => handler.HandleAsync(new UpdateMaterialCommand(material.MaterialId, category.MaterialCategoryId, null, null, null, null))))
            .Code.Should().Be("MATERIAL_CATEGORY_INACTIVE");
    }

    [Fact]
    public async Task ListMaterials_filters_sorts_and_pages()
    {
        var category = _store.SeedCategory();
        _store.SeedMaterial(category, "B-1", "Bóng đèn");
        _store.SeedMaterial(category, "A-1", "Ắc quy");
        _store.SeedMaterial(category, "C-1", "Cáp");

        var page = await new ListMaterialsQueryHandler(_store)
            .HandleAsync(new MaterialSearchCriteria(1, 2, SortField: MaterialSortField.Code, SortDescending: true));

        page.TotalItems.Should().Be(3);
        page.Items.Select(m => m.Code).Should().Equal("C-1", "B-1");
    }
}
