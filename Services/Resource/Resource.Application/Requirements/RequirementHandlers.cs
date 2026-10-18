using Resource.Application.Catalog;
using Resource.Application.Scope;
using Resource.Domain;
using Resource.Domain.Catalog;
using Resource.Domain.Inventory;

namespace Resource.Application.Requirements;

/// <summary><c>MaterialRequirement</c> schema plus the catalog fields clients need to render it.</summary>
public sealed record MaterialRequirementDto(
    Guid Id,
    Guid WorkAreaId,
    Guid MaterialId,
    string MaterialCode,
    string MaterialName,
    string Unit,
    decimal RequiredQuantity,
    decimal ExistingQuantity,
    RequirementStatus Status,
    string? Note,
    long Version)
{
    public static MaterialRequirementDto From(MaterialRequirement requirement, Material material) =>
        new(requirement.MaterialRequirementId, requirement.WorkAreaId, requirement.MaterialId, material.Code, material.Name,
            material.Unit, requirement.RequiredQuantity, requirement.ExistingQuantity, requirement.Status, requirement.Note,
            requirement.Version);
}

public interface IMaterialRequirementStore
{
    Task<IReadOnlyList<MaterialRequirement>> ListByWorkAreaAsync(Guid workAreaId, CancellationToken cancellationToken = default);

    Task<MaterialRequirement?> GetAsync(Guid requirementId, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Guid workAreaId, Guid materialId, CancellationToken cancellationToken = default);

    void Add(MaterialRequirement requirement);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed record CreateMaterialRequirementCommand(Guid WorkAreaId, Guid MaterialId, decimal RequiredQuantity, decimal? ExistingQuantity, string? Note);

public sealed record UpdateMaterialRequirementCommand(
    Guid RequirementId,
    long? Version,
    decimal? RequiredQuantity,
    decimal? ExistingQuantity,
    string? Note,
    RequirementStatus? Status);

/// <summary>F-RES-03 <c>listMaterialRequirements</c> (USER, same parish).</summary>
public sealed class ListMaterialRequirementsQueryHandler(
    WorkAreaAccess access,
    IMaterialRequirementStore store,
    IMaterialCatalogStore catalog)
{
    public async Task<IReadOnlyList<MaterialRequirementDto>> HandleAsync(
        Guid workAreaId,
        RequirementStatus? status,
        CancellationToken cancellationToken = default)
    {
        await access.ForReadAsync(workAreaId, cancellationToken);

        var requirements = (await store.ListByWorkAreaAsync(workAreaId, cancellationToken))
            .Where(requirement => status is null || requirement.Status == status)
            .ToList();
        var materials = (await catalog.GetMaterialsAsync(requirements.Select(r => r.MaterialId).ToList(), cancellationToken))
            .ToDictionary(material => material.MaterialId);

        return requirements
            .Select(requirement => MaterialRequirementDto.From(requirement, materials[requirement.MaterialId]))
            .OrderBy(dto => dto.MaterialName, StringComparer.CurrentCulture)
            .ToList();
    }
}

/// <summary>
/// F-RES-03 <c>createMaterialRequirement</c> (MO of the work area): material must be ACTIVE, quantity &gt; 0,
/// one requirement per (work area, material) — a cancelled one is re-activated through PATCH instead.
/// </summary>
public sealed class CreateMaterialRequirementCommandHandler(
    WorkAreaAccess access,
    IMaterialRequirementStore store,
    IMaterialCatalogStore catalog)
{
    public async Task<MaterialRequirementDto> HandleAsync(
        CreateMaterialRequirementCommand command,
        CancellationToken cancellationToken = default)
    {
        await access.ForMaterialOfficerWriteAsync(command.WorkAreaId, cancellationToken);

        var material = await catalog.GetMaterialAsync(command.MaterialId, cancellationToken)
            ?? throw DomainRuleException.Validation("MATERIAL_UNKNOWN", "The material does not exist.");
        if (material.Status != CatalogStatus.Active)
        {
            throw DomainRuleException.Validation("MATERIAL_INACTIVE", "The material is inactive.");
        }

        // Field validation (422) before the uniqueness conflict (409).
        var requirement = MaterialRequirement.Create(
            command.WorkAreaId,
            command.MaterialId,
            command.RequiredQuantity,
            command.ExistingQuantity ?? 0,
            command.Note,
            DateTimeOffset.UtcNow);

        if (await store.ExistsAsync(command.WorkAreaId, command.MaterialId, cancellationToken))
        {
            throw new DomainRuleException(
                "REQUIREMENT_ALREADY_EXISTS",
                "This work area already has a requirement for the material. Update it instead.");
        }

        store.Add(requirement);
        await store.SaveChangesAsync(cancellationToken);
        return MaterialRequirementDto.From(requirement, material);
    }
}

/// <summary>F-RES-03 <c>updateMaterialRequirement</c> (MO of the work area): quantity, note, cancel/re-activate.</summary>
public sealed class UpdateMaterialRequirementCommandHandler(
    WorkAreaAccess access,
    IMaterialRequirementStore store,
    IMaterialCatalogStore catalog)
{
    public async Task<MaterialRequirementDto> HandleAsync(
        UpdateMaterialRequirementCommand command,
        CancellationToken cancellationToken = default)
    {
        var requirement = await store.GetAsync(command.RequirementId, cancellationToken)
            ?? throw DomainRuleException.NotFound("REQUIREMENT_NOT_FOUND", "The material requirement does not exist.");

        // Scope first: an officer of another parish must not learn that the id exists.
        await access.ForMaterialOfficerWriteAsync(requirement.WorkAreaId, cancellationToken);
        requirement.EnsureVersion(command.Version);

        var material = await catalog.GetMaterialAsync(requirement.MaterialId, cancellationToken)
            ?? throw new InvalidOperationException("Requirement points to a missing material.");

        if (command.Status == RequirementStatus.Active &&
            requirement.Status != RequirementStatus.Active &&
            material.Status != CatalogStatus.Active)
        {
            throw DomainRuleException.Validation("MATERIAL_INACTIVE", "An inactive material cannot be required again.");
        }

        requirement.Update(command.RequiredQuantity, command.ExistingQuantity, command.Note, command.Status, DateTimeOffset.UtcNow);
        await store.SaveChangesAsync(cancellationToken);
        return MaterialRequirementDto.From(requirement, material);
    }
}
