using Microsoft.EntityFrameworkCore;
using ParishCoordination.Domain.Entities;

namespace ParishCoordination.Infrastructure.Data;

public sealed class ParishCoordinationDbContext(DbContextOptions<ParishCoordinationDbContext> options)
    : DbContext(options)
{
    public DbSet<Parish> Parishes => Set<Parish>();

    public DbSet<Community> Communities => Set<Community>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ParishCoordinationDbContext).Assembly);
    }
}
