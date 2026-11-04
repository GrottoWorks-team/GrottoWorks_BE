namespace Resource.Contracts.Catalog;

public sealed record InternalMaterialDto(Guid MaterialId, string Code, string Name, string Unit, string Status);
