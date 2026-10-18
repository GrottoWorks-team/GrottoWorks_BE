using Microsoft.EntityFrameworkCore;
using Resource.Domain.Catalog;
using Resource.Domain.Scope;
using Resource.Domain.Inventory;
using Resource.Domain.Purchases;

namespace Resource.Infrastructure.Data;

/// <summary>resource_db — tables owned by the Resource service (spec section 3, ERD v3.6).</summary>
public sealed class ResourceDbContext(DbContextOptions<ResourceDbContext> options) : DbContext(options)
{
    public DbSet<MaterialCategory> MaterialCategories => Set<MaterialCategory>();

    public DbSet<Material> Materials => Set<Material>();

    public DbSet<WorkAreaScope> WorkAreaScopes => Set<WorkAreaScope>();

    public DbSet<MaterialRequirement> MaterialRequirements => Set<MaterialRequirement>();

    public DbSet<MaterialTransaction> MaterialTransactions => Set<MaterialTransaction>();

    public DbSet<PurchaseRequest> PurchaseRequests => Set<PurchaseRequest>();

    public DbSet<PurchaseRequestStateChange> PurchaseRequestStateChanges => Set<PurchaseRequestStateChange>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ResourceDbContext).Assembly);
    }
}
