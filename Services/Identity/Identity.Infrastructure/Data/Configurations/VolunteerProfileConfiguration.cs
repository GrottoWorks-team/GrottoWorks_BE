using Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Data.Configurations;

public sealed class VolunteerProfileConfiguration : IEntityTypeConfiguration<VolunteerProfile>
{
    public void Configure(EntityTypeBuilder<VolunteerProfile> builder)
    {
        builder.ToTable("volunteer_profile");

        builder.HasKey(profile => profile.VolunteerUserId);

        builder.Property(profile => profile.VolunteerUserId)
            .HasColumnName("volunteer_user_id");

        // Cross-service reference to ParishCoordination — no FK (spec 2.1).
        builder.Property(profile => profile.CommunityId)
            .HasColumnName("community_id")
            .IsRequired();

        builder.Property(profile => profile.Introduction)
            .HasColumnName("introduction");

        builder.Property(profile => profile.AvailabilityNote)
            .HasColumnName("availability_note");
    }
}
