using Microsoft.EntityFrameworkCore;

namespace Resource.Infrastructure.Data;

/// <summary>resource_db — tables owned by the Resource service (spec section 3, ERD v3.6).</summary>
public sealed class ResourceDbContext(DbContextOptions<ResourceDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ResourceDbContext).Assembly);
    }
}
