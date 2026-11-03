namespace TaskExecution.Application.Support;

public sealed record SupportMaterialAvailability(Guid WorkAreaId,Guid ParishId,Guid SeasonId,Guid? MaterialOfficerUserId,
    long ScopeVersion,Guid MaterialId,string MaterialStatus,decimal UnallocatedQuantity,DateTimeOffset CalculatedAt);

public interface ISupportMaterialAvailability
{
    Task<IReadOnlyList<SupportMaterialAvailability>> GetAsync(Guid workAreaId,IReadOnlyCollection<Guid> materialIds,CancellationToken cancellationToken);
}
