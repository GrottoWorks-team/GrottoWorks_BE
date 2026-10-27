using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Resource.Domain.Contributions;

namespace Resource.Infrastructure.Data.Configurations;

/// <summary><c>donor</c> (ERD v3.6.2, F-RES-12) + extension column <c>parish_id</c> (tenant scope).</summary>
public sealed class DonorConfiguration : IEntityTypeConfiguration<Donor>
{
    public const string UserIndex = "ux_donor_user";

    public void Configure(EntityTypeBuilder<Donor> builder)
    {
        builder.ToTable("donor");

        builder.HasKey(donor => donor.DonorId);
        builder.Property(donor => donor.DonorId).HasColumnName("donor_id").ValueGeneratedNever();
        // parish_id and user ids reference other services: no FK (spec 2.1).
        builder.Property(donor => donor.ParishId).HasColumnName("parish_id").IsRequired();
        builder.Property(donor => donor.UserId).HasColumnName("user_id");
        builder.Property(donor => donor.DonorName).HasColumnName("donor_name").HasMaxLength(Donor.MaxNameLength).IsRequired();
        builder.Property(donor => donor.Phone).HasColumnName("phone").HasMaxLength(Donor.MaxPhoneLength);
        builder.Property(donor => donor.Email).HasColumnName("email").HasMaxLength(Donor.MaxEmailLength);
        builder.Property(donor => donor.Address).HasColumnName("address").HasMaxLength(Donor.MaxAddressLength);
        builder.Property(donor => donor.Note).HasColumnName("donor_note");
        builder.Property(donor => donor.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(donor => donor.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(donor => donor.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        builder.Property(donor => donor.Version).IsRowVersion();

        builder.HasIndex(donor => donor.UserId).IsUnique().HasDatabaseName(UserIndex);
        builder.HasIndex(donor => new { donor.ParishId, donor.DonorName }, "ix_donor_parish_name");
    }
}
