using BuildingBlocks.Security;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Users;
using TaskExecution.Domain;
using TaskExecution.Domain.Scopes;
using TaskExecution.Domain.Support;

namespace TaskExecution.Application.Support;

public sealed class ValidateSupportMaterialOffers(IScopeProjectionStore scopes,ISupportMaterialAvailability inventory)
{
    public async Task HandleAsync(SupportRequest request,WorkAreaScope target,IdentityUser caller,IReadOnlyList<SupportMaterialOfferInput> offers,CancellationToken ct)
    {
        request.ValidateMaterialOffers(offers);
        if(offers.Count==0) return;
        if(caller.Role!=GrottoWorksRoles.MaterialOfficer)
            throw DomainRuleException.Forbidden("WORK_AREA_ACCESS_DENIED","Only the source material officer may offer its stock.");
        var demands=request.Materials.ToDictionary(demand=>demand.Id);
        foreach(var group in offers.GroupBy(offer=>offer.SourceWorkAreaId))
        {
            var source=await scopes.GetWorkAreaAsync(group.Key,ct)
                ?? throw DomainRuleException.NotFound("WORK_AREA_NOT_FOUND","The source work area is missing.");
            if(source.ParishId!=target.ParishId) throw DomainRuleException.Forbidden("PARISH_ACCESS_DENIED","Source and target must belong to the same parish.");
            if(source.SeasonId!=target.SeasonId) throw DomainRuleException.Validation("SUPPORT_TRANSFER_OTHER_SEASON","Source and target must belong to the same season.");
            if(source.MaterialOfficerUserId!=caller.UserId)
                throw DomainRuleException.Forbidden("WORK_AREA_ACCESS_DENIED","Only the assigned source material officer may offer its stock.");
            var ids=group.Select(offer=>demands[offer.RequestMaterialId].MaterialId).Distinct().ToArray();
            var available=(await inventory.GetAsync(source.WorkAreaId,ids,ct)).ToDictionary(row=>row.MaterialId);
            foreach(var offer in group)
            {
                var id=demands[offer.RequestMaterialId].MaterialId;
                if(!available.TryGetValue(id,out var balance)) throw DomainRuleException.NotFound("MATERIAL_NOT_FOUND","The material does not exist in Resource.");
                if(balance.WorkAreaId!=source.WorkAreaId || balance.ParishId!=source.ParishId || balance.SeasonId!=source.SeasonId ||
                    balance.ScopeVersion<source.SourceVersion || balance.MaterialOfficerUserId!=caller.UserId)
                    throw new DomainRuleException("RESOURCE_SERVICE_UNAVAILABLE","Resource scope is not synchronized with the offered work area.",DomainErrorKind.ServiceUnavailable);
                if(balance.MaterialStatus!="ACTIVE") throw new DomainRuleException("MATERIAL_INACTIVE","The material is inactive.");
                if(offer.OfferedQuantity>balance.UnallocatedQuantity)
                    throw new DomainRuleException("MATERIAL_INSUFFICIENT_AVAILABLE","The offer exceeds currently unallocated stock.");
            }
        }
    }
}
