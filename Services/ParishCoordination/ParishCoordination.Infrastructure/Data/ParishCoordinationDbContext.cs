using Microsoft.EntityFrameworkCore;
using ParishCoordination.Domain.Entities;

namespace ParishCoordination.Infrastructure.Data;

public sealed class ParishCoordinationDbContext(DbContextOptions<ParishCoordinationDbContext> options)
    : DbContext(options)
{
    public DbSet<Parish> Parishes => Set<Parish>();

    public DbSet<Community> Communities => Set<Community>();

    public DbSet<Season> Seasons => Set<Season>();

    public DbSet<WorkArea> WorkAreas => Set<WorkArea>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ParishCoordinationDbContext).Assembly);
    }
}
