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

    public DbSet<TaskProjection> TaskProjections => Set<TaskProjection>();

    public DbSet<MaterialRequirement> MaterialRequirements => Set<MaterialRequirement>();

    public DbSet<MaterialTransaction> MaterialTransactions => Set<MaterialTransaction>();

    public DbSet<MaterialAllocation> MaterialAllocations => Set<MaterialAllocation>();

    public DbSet<TaskMaterialReport> TaskMaterialReports => Set<TaskMaterialReport>();

    public DbSet<SupportTransfer> SupportTransfers => Set<SupportTransfer>();

    public DbSet<PurchaseRequest> PurchaseRequests => Set<PurchaseRequest>();

    public DbSet<PurchaseRequestStateChange> PurchaseRequestStateChanges => Set<PurchaseRequestStateChange>();

    public DbSet<Donor> Donors => Set<Donor>();

    public DbSet<Donation> Donations => Set<Donation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ResourceDbContext).Assembly);
    }
}
