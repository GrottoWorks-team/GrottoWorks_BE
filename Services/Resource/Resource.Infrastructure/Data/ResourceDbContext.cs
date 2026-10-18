using Microsoft.EntityFrameworkCore;
using Resource.Domain.Catalog;

namespace Resource.Infrastructure.Data;

/// <summary>resource_db — tables owned by the Resource service (spec section 3, ERD v3.6).</summary>
public sealed class ResourceDbContext(DbContextOptions<ResourceDbContext> options) : DbContext(options)
{
    public DbSet<MaterialCategory> MaterialCategories => Set<MaterialCategory>();

    public DbSet<Material> Materials => Set<Material>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ResourceDbContext).Assembly);
    }
}
