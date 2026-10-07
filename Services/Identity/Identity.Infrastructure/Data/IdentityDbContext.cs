using Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Data;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<Skill> Skills => Set<Skill>();

    public DbSet<VolunteerProfile> VolunteerProfiles => Set<VolunteerProfile>();

    public DbSet<VolunteerAvailability> VolunteerAvailabilities => Set<VolunteerAvailability>();

    public DbSet<VolunteerSkill> VolunteerSkills => Set<VolunteerSkill>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
    }
}
