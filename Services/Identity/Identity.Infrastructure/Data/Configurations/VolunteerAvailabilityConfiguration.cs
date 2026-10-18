using Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Data.Configurations;

public sealed class VolunteerAvailabilityConfiguration : IEntityTypeConfiguration<VolunteerAvailability>
{
    public void Configure(EntityTypeBuilder<VolunteerAvailability> builder)
    {
        builder.ToTable(
            "volunteer_availability",
            table => table.HasCheckConstraint(
                "volunteer_availability_valid_range",
                "\"available_to\" > \"available_from\""));

        builder.HasKey(slot => slot.VolunteerAvailabilityId);

        // Ids are created in the domain (Guid.NewGuid). Without ValueGeneratedNever EF treats a new child
        // found through the AppUser collection as an existing row and issues an UPDATE (0 rows → concurrency error).
        builder.Property(slot => slot.VolunteerAvailabilityId)
            .ValueGeneratedNever()
            .HasColumnName("volunteer_availability_id");

        builder.Property(slot => slot.VolunteerUserId)
            .HasColumnName("volunteer_user_id")
            .IsRequired();

        builder.Property(slot => slot.AvailableFrom)
            .HasColumnName("available_from")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(slot => slot.AvailableTo)
            .HasColumnName("available_to")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(slot => slot.AvailabilityNote)
            .HasColumnName("availability_note");

        builder.HasIndex(slot => slot.VolunteerUserId)
            .HasDatabaseName("ix_volunteer_availability_volunteer_user_id");
    }
}
