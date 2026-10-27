using Microsoft.Extensions.DependencyInjection;
using Resource.Application.Catalog;
using Resource.Application.Scope;
using Resource.Application.Requirements;
using Resource.Application.Inventory;
using Resource.Application.Shortages;
using Resource.Application.Purchases;
using Resource.Application.Contributions;

namespace Resource.Application;

/// <summary>
/// Plain handler classes registered as scoped services (no MediatR — plan.md "Pattern Application").
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // F-RES-02 material catalog
        services.AddScoped<ListMaterialCategoriesQueryHandler>();
        services.AddScoped<CreateMaterialCategoryCommandHandler>();
        services.AddScoped<UpdateMaterialCategoryCommandHandler>();
        services.AddScoped<ListMaterialsQueryHandler>();
        services.AddScoped<CreateMaterialCommandHandler>();
        services.AddScoped<UpdateMaterialCommandHandler>();

        // F-RES-01 work-area scope projection
        services.AddScoped<ApplyWorkAreaSnapshotHandler>();
        services.AddScoped<ApplySeasonStatusHandler>();
        services.AddScoped<ApplyTaskSnapshotHandler>();
        services.AddScoped<WorkAreaAccess>();

        // F-RES-03 material requirements
        services.AddScoped<ListMaterialRequirementsQueryHandler>();
        services.AddScoped<CreateMaterialRequirementCommandHandler>();
        services.AddScoped<UpdateMaterialRequirementCommandHandler>();

        // F-RES-04 stock ledger
        services.AddScoped<InventoryReader>();
        services.AddScoped<RecordOpeningStockCommandHandler>();
        services.AddScoped<ListInventoryQueryHandler>();
        services.AddScoped<GetMaterialHistoryQueryHandler>();

        // F-RES-15 allocations
        services.AddScoped<AllocationProjector>();
        services.AddScoped<AllocateMaterialCommandHandler>();
        services.AddScoped<ListMaterialAllocationsQueryHandler>();

        // F-RES-16 inventory adjustments
        services.AddScoped<CreateInventoryAdjustmentCommandHandler>();

        // F-RES-17 task material reports
        services.AddScoped<MaterialReportContext>();
        services.AddScoped<SubmitMaterialReportCommandHandler>();
        services.AddScoped<ListTaskMaterialReportsQueryHandler>();
        services.AddScoped<VerifyMaterialReportCommandHandler>();
        services.AddScoped<RequestReportRevisionCommandHandler>();

        // F-RES-20 support transfers
        services.AddScoped<StartSupportTransferCommandHandler>();
        services.AddScoped<ConfirmTransferReceiptCommandHandler>();

        // F-RES-05 shortages
        services.AddScoped<ShortageTracker>();
        services.AddScoped<GetWorkAreaShortagesQueryHandler>();
        services.AddScoped<ListMaterialNeedsQueryHandler>();

        // F-RES-06/07 purchase requests
        services.AddScoped<PurchaseRequestLoader>();
        services.AddScoped<PurchaseShortageGuard>();
        services.AddScoped<ListPurchaseRequestsQueryHandler>();
        services.AddScoped<GetPurchaseRequestQueryHandler>();
        services.AddScoped<GetPurchaseRequestHistoryQueryHandler>();
        services.AddScoped<CreatePurchaseRequestCommandHandler>();
        services.AddScoped<UpdatePurchaseRequestCommandHandler>();
        services.AddScoped<SubmitPurchaseRequestCommandHandler>();
        services.AddScoped<DecidePurchaseRequestCommandHandler>();

        // F-RES-12 donors
        services.AddScoped<DonorAccess>();
        services.AddScoped<ListDonorsQueryHandler>();
        services.AddScoped<CreateDonorCommandHandler>();
        services.AddScoped<UpdateDonorCommandHandler>();

        // F-RES-13 direct donations
        services.AddScoped<DonationProjector>();
        services.AddScoped<MaterialOfficerScope>();
        services.AddScoped<DonationReceiptPoster>();
        services.AddScoped<RecordDirectDonationCommandHandler>();
        services.AddScoped<ListDonationsQueryHandler>();
        services.AddScoped<GetDonationQueryHandler>();
        services.AddScoped<ListMyContributionsQueryHandler>();

        // F-RES-13b pledges
        services.AddScoped<VolunteerDonorResolver>();
        services.AddScoped<ListContributionNeedsQueryHandler>();
        services.AddScoped<PledgeMaterialCommandHandler>();
        services.AddScoped<PledgeMoneyCommandHandler>();
        services.AddScoped<CancelPledgeCommandHandler>();

        // F-RES-18 loan offers and borrowed items
        services.AddScoped<BorrowedItemAccess>();
        services.AddScoped<OfferItemForLoanCommandHandler>();
        services.AddScoped<ListLoanOffersQueryHandler>();
        services.AddScoped<ListBorrowedItemsQueryHandler>();
        services.AddScoped<ListOverdueBorrowedItemsQueryHandler>();
        services.AddScoped<BorrowedItemCommandHandler>();
        services.AddScoped<BorrowedItemReminderJob>();

        // F-RES-13c receipts of pledges
        services.AddScoped<RecordDonationReceiptCommandHandler>();

        return services;
    }
}
