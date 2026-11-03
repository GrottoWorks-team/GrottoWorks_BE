using Microsoft.EntityFrameworkCore;
using Resource.Domain.Catalog;
using Resource.Domain.Scope;
using Resource.Domain.Inventory;
using Resource.Domain.Purchases;
using Resource.Domain.Contributions;

namespace Resource.Infrastructure.Data;

/// <summary>resource_db — tables owned by the Resource service (spec section 3, ERD v3.6).</summary>
public sealed class ResourceDbContext(DbContextOptions<ResourceDbContext> options) : DbContext(options)
{
    public DbSet<MaterialCategory> MaterialCategories => Set<MaterialCategory>();

    public DbSet<Material> Materials => Set<Material>();

    public DbSet<WorkAreaScope> WorkAreaScopes => Set<WorkAreaScope>();

    public DbSet<SeasonScope> SeasonScopes => Set<SeasonScope>();

    public DbSet<TaskProjection> TaskProjections => Set<TaskProjection>();

    public DbSet<MaterialRequirement> MaterialRequirements => Set<MaterialRequirement>();

    public DbSet<MaterialTransaction> MaterialTransactions => Set<MaterialTransaction>();

    public DbSet<MaterialAllocation> MaterialAllocations => Set<MaterialAllocation>();

    public DbSet<TaskMaterialReport> TaskMaterialReports => Set<TaskMaterialReport>();

    public DbSet<SupportTransfer> SupportTransfers => Set<SupportTransfer>();

    public DbSet<PurchaseRequest> PurchaseRequests => Set<PurchaseRequest>();

    public DbSet<PurchaseRequestStateChange> PurchaseRequestStateChanges => Set<PurchaseRequestStateChange>();

    public DbSet<Purchase> Purchases => Set<Purchase>();

    public DbSet<Donor> Donors => Set<Donor>();

    public DbSet<Donation> Donations => Set<Donation>();

    public DbSet<BorrowedItem> BorrowedItems => Set<BorrowedItem>();

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        var references = ChangeTracker.Entries<Resource.Domain.Files.AttachmentIntent>()
            .Where(x => x.State == EntityState.Modified && x.Entity.State == "FINALIZE")
            .Select(x => x.Entity.ReferenceId).Distinct().Order().ToArray();
        if (references.Length == 0) return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        var ownsTransaction = Database.CurrentTransaction is null;
        var transaction = ownsTransaction ? await Database.BeginTransactionAsync(cancellationToken) : Database.CurrentTransaction!;
        try
        {
            foreach (var reference in references)
                await Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock_shared(hashtextextended({reference.ToString()}, 712));", cancellationToken);
            var count = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            if (ownsTransaction) await transaction.CommitAsync(cancellationToken);
            return count;
        }
        finally { if (ownsTransaction) await transaction.DisposeAsync(); }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // integration_event_outbox is configured by Outbox/OutboxMessageConfiguration (same assembly).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ResourceDbContext).Assembly);
    }
}
