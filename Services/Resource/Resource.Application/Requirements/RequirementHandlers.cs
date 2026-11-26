using System.Security.Cryptography;
using Resource.Application.Catalog;
using Resource.Application.Inventory;
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
    public static MaterialRequirementDto From(QuantityByMaterial requirement, Material material) =>
        new(StableId(requirement.WorkAreaId, requirement.MaterialId), requirement.WorkAreaId, requirement.MaterialId,
            material.Code, material.Name, material.Unit, requirement.Quantity, 0, RequirementStatus.Active, null, 0);

    private static Guid StableId(Guid workAreaId, Guid materialId)
    {
        Span<byte> identity = stackalloc byte[32];
        workAreaId.TryWriteBytes(identity[..16], bigEndian: true, out _);
        materialId.TryWriteBytes(identity[16..], bigEndian: true, out _);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(identity, hash);
        // Version 8 UUID and standard variant for an identity derived from the aggregate key.
        hash[6] = (byte)((hash[6] & 0x0f) | 0x80);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        return new Guid(hash[..16], bigEndian: true);
    }
}

public interface IMaterialRequirementStore
{
    Task<IReadOnlyList<QuantityByMaterial>> ListByWorkAreaAsync(Guid workAreaId, CancellationToken cancellationToken = default);
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

        if (status is not (null or RequirementStatus.Active)) return [];
        var requirements = await store.ListByWorkAreaAsync(workAreaId, cancellationToken);
        var materials = (await catalog.GetMaterialsAsync(requirements.Select(r => r.MaterialId).ToList(), cancellationToken))
            .ToDictionary(material => material.MaterialId);

        return requirements
            .Select(requirement => MaterialRequirementDto.From(requirement, materials[requirement.MaterialId]))
            .OrderBy(dto => dto.MaterialName, StringComparer.CurrentCulture)
            .ToList();
    }
}

/// <summary>Former manual create command. Area requirements now derive from task material plans.</summary>
public sealed class CreateMaterialRequirementCommandHandler
{
    public Task<MaterialRequirementDto> HandleAsync(
        CreateMaterialRequirementCommand command,
        CancellationToken cancellationToken = default) =>
        throw DomainRuleException.Validation(
            "MANUAL_MATERIAL_REQUIREMENT_DISABLED",
            "Material requirements must be declared from the real materials of each task.");
}

/// <summary>Former manual update command. Area requirements now derive from task material plans.</summary>
public sealed class UpdateMaterialRequirementCommandHandler
{
    public Task<MaterialRequirementDto> HandleAsync(
        UpdateMaterialRequirementCommand command,
        CancellationToken cancellationToken = default) =>
        throw DomainRuleException.Validation(
            "MANUAL_MATERIAL_REQUIREMENT_DISABLED",
            "Material requirements must be declared from the real materials of each task.");
}
