using BuildingBlocks.Security;
using Resource.Application.Abstractions;
using Resource.Application.Catalog;
using Resource.Application.Scope;
using Resource.Application.Shortages;
using Resource.Domain;
using Resource.Domain.Catalog;
using Resource.Domain.Inventory;
using Resource.Domain.Scope;

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
/// F-RES-03 <c>createMaterialRequirement</c>: the material estimate of a work area (planning figure used to
/// derive "required"/shortage before tasks declare their own materials). The material must be ACTIVE and each
/// (work area, material) pair exists once — a cancelled one is re-activated through PATCH instead.
/// </summary>
public sealed class CreateMaterialRequirementCommandHandler(
    WorkAreaAccess access,
    IMaterialRequirementStore store,
    IMaterialCatalogStore catalog,
    ShortageTracker shortages,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork)
{
    public Task<MaterialRequirementDto> HandleAsync(
        CreateMaterialRequirementCommand command,
        CancellationToken cancellationToken = default) =>
        unitOfWork.ExecuteAsync(async () =>
        {
            var scope = await access.ForReadAsync(command.WorkAreaId, cancellationToken);
            MaterialEstimatePolicy.EnsureCanEdit(scope, currentUser);

            var material = (await catalog.GetMaterialsAsync([command.MaterialId], cancellationToken)).SingleOrDefault()
                ?? throw DomainRuleException.Validation("MATERIAL_UNKNOWN", "The material does not exist.");
            if (material.Status != CatalogStatus.Active)
            {
                throw DomainRuleException.Validation("MATERIAL_INACTIVE", "Only an ACTIVE material can be estimated.");
            }

            if (await store.ExistsAsync(command.WorkAreaId, command.MaterialId, cancellationToken))
            {
                throw new DomainRuleException(
                    "REQUIREMENT_EXISTS",
                    "This material already has an estimate in the work area; edit that line instead.",
                    DomainErrorKind.Conflict);
            }

            var before = await shortages.CaptureAsync(scope, command.MaterialId, cancellationToken);
            var requirement = MaterialRequirement.Create(
                scope.WorkAreaId, command.MaterialId, command.RequiredQuantity, command.ExistingQuantity ?? 0,
                command.Note, DateTimeOffset.UtcNow);
            store.Add(requirement);
            await store.SaveChangesAsync(cancellationToken);
            await shortages.PublishIfChangedAsync(before, cancellationToken);

            return MaterialRequirementDto.From(requirement, material);
        }, cancellationToken);
}

/// <summary>F-RES-03 <c>updateMaterialRequirement</c>: quantity, note, cancel/re-activate the estimate.</summary>
public sealed class UpdateMaterialRequirementCommandHandler(
    WorkAreaAccess access,
    IMaterialRequirementStore store,
    IMaterialCatalogStore catalog,
    ShortageTracker shortages,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork)
{
    public Task<MaterialRequirementDto> HandleAsync(
        UpdateMaterialRequirementCommand command,
        CancellationToken cancellationToken = default) =>
        unitOfWork.ExecuteAsync(async () =>
        {
            var requirement = await store.GetAsync(command.RequirementId, cancellationToken)
                ?? throw DomainRuleException.NotFound("REQUIREMENT_NOT_FOUND", "The material estimate does not exist.");
            var scope = await access.ForReadAsync(requirement.WorkAreaId, cancellationToken);
            MaterialEstimatePolicy.EnsureCanEdit(scope, currentUser);
            requirement.EnsureVersion(command.Version);

            var material = (await catalog.GetMaterialsAsync([requirement.MaterialId], cancellationToken)).SingleOrDefault()
                ?? throw DomainRuleException.Validation("MATERIAL_UNKNOWN", "The material does not exist.");

            var before = await shortages.CaptureAsync(scope, requirement.MaterialId, cancellationToken);
            requirement.Update(command.RequiredQuantity, command.ExistingQuantity, command.Note, command.Status, DateTimeOffset.UtcNow);
            await store.SaveChangesAsync(cancellationToken);
            await shortages.PublishIfChangedAsync(before, cancellationToken);

            return MaterialRequirementDto.From(requirement, material);
        }, cancellationToken);
}

/// <summary>
/// Who may change the material estimate of a work area: its material officer (while the season is not archived), or
/// the parish committee/admin while the season is still being planned (BR: estimates are frozen once the season starts).
/// </summary>
internal static class MaterialEstimatePolicy
{
    public static void EnsureCanEdit(WorkAreaScope scope, ICurrentUser user)
    {
        WorkAreaAccess.EnsureWritable(scope);

        var isOfficer = user.Role == GrottoWorksRoles.MaterialOfficer && scope.MaterialOfficerUserId == user.UserId;
        var isPlanningParish =
            user.Role is GrottoWorksRoles.Parish or GrottoWorksRoles.Admin && scope.SeasonStatus == SeasonStatus.Planning;
        if (!isOfficer && !isPlanningParish)
        {
            throw DomainRuleException.Forbidden(
                "WORK_AREA_SCOPE_DENIED",
                "Only the material officer of this work area (or the parish while the season is planned) can change the material estimate.");
        }
    }
}
