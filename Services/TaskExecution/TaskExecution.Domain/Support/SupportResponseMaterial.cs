namespace TaskExecution.Domain.Support;

public enum SupportMaterialStatus { Pending,Approved,Rejected,Transferred,Cancelled }
public sealed record SupportMaterialOfferInput(Guid RequestMaterialId,Guid SourceWorkAreaId,decimal OfferedQuantity,string? Note=null);

public sealed class SupportResponseMaterial
{
    private SupportResponseMaterial() { }
    public Guid Id {get;private set;}
    public Guid ResponseId {get;private set;}
    public Guid RequestId {get;private set;}
    public Guid RequestMaterialId {get;private set;}
    public Guid SourceWorkAreaId {get;private set;}
    public Guid MaterialId {get;private set;}
    public decimal OfferedQuantity {get;private set;}
    public decimal? ApprovedQuantity {get;private set;}
    public decimal? ReceivedUsableQuantity {get;private set;}
    public decimal? ReceivedUnusableQuantity {get;private set;}
    public SupportMaterialStatus Status {get;private set;}=SupportMaterialStatus.Pending;
    public Guid? ApprovedBy {get;private set;}
    public DateTimeOffset? ApprovedAt {get;private set;}
    public Guid? ReceivedConfirmedBy {get;private set;}
    public DateTimeOffset? ReceivedConfirmedAt {get;private set;}
    public string? Note {get;private set;}
    public int Version {get;private set;}=1;

    internal static SupportResponseMaterial Offer(Guid responseId,Guid requestId,Guid materialId,SupportMaterialOfferInput input)
    {
        ValidateOffer(input);
        return new SupportResponseMaterial
        {
            Id=Guid.NewGuid(),ResponseId=responseId,RequestId=requestId,RequestMaterialId=input.RequestMaterialId,
            SourceWorkAreaId=input.SourceWorkAreaId,MaterialId=materialId,OfferedQuantity=input.OfferedQuantity,
            Note=string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim()
        };
    }

    internal static void ValidateOffer(SupportMaterialOfferInput input)
    {
        if(input.RequestMaterialId==Guid.Empty || input.SourceWorkAreaId==Guid.Empty || input.OfferedQuantity<=0 ||
            input.OfferedQuantity>9999999999.99m || decimal.Round(input.OfferedQuantity,2)!=input.OfferedQuantity || input.Note?.Length>2000)
            throw DomainRuleException.Validation("SUPPORT_MATERIAL_OFFER_INVALID","Offer requires valid identities, a positive (12,2) quantity and note up to 2000 characters.");
    }
}
