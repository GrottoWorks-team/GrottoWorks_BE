using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskExecution.Domain.Assignments;
using TaskExecution.Domain.Support;
using TaskExecution.Infrastructure.Data;

namespace TaskExecution.Infrastructure.Support;

public sealed class SupportResponseConfiguration : IEntityTypeConfiguration<SupportResponse>
{
    public void Configure(EntityTypeBuilder<SupportResponse> b)
    {
        b.ToTable("support_response");b.HasKey(row=>row.Id);
        b.Property(row=>row.Id).HasColumnName("support_response_id").ValueGeneratedNever();
        b.Property(row=>row.RequestId).HasColumnName("support_request_id");
        b.HasAlternateKey(row=>new {row.RequestId,row.Id}).HasName("ak_support_response_request_id");
        b.Property(row=>row.SubmittedBy).HasColumnName("submitted_by_user_id");
        b.Property(row=>row.VolunteerId).HasColumnName("volunteer_user_id");
        b.Property(row=>row.RespondingCommunityId).HasColumnName("responding_community_id");
        b.Property(row=>row.Status).HasColumnName("response_status").HasMaxLength(30).HasConversion(EnumText.Converter<SupportResponseStatus>());
        b.Property(row=>row.ResponseQuantity).HasColumnName("response_quantity");
        b.Property(row=>row.Note).HasColumnName("response_note").HasMaxLength(2000);
        b.Property(row=>row.CreatedAt).HasColumnName("created_at");
        b.Property(row=>row.Version).HasColumnName("version").HasDefaultValue(1).IsConcurrencyToken();
        b.HasMany(row=>row.Members).WithOne().HasForeignKey(row=>new {row.RequestId,row.ResponseId})
            .HasPrincipalKey(row=>new {row.RequestId,row.Id}).OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_support_member_response");
        b.Navigation(row=>row.Members).HasField("members").UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(row=>new {row.RequestId,row.CreatedAt,row.Id}).HasDatabaseName("ix_support_response_request_created");
    }
}

public sealed class SupportResponseMemberConfiguration : IEntityTypeConfiguration<SupportResponseMember>
{
    public const string ActiveVolunteerIndex="ux_support_member_request_volunteer_active";
    public void Configure(EntityTypeBuilder<SupportResponseMember> b)
    {
        b.ToTable("support_response_member");b.HasKey(row=>row.Id);
        b.Property(row=>row.Id).HasColumnName("support_response_member_id").ValueGeneratedNever();
        b.Property(row=>row.ResponseId).HasColumnName("support_response_id");b.Property(row=>row.RequestId).HasColumnName("support_request_id");
        b.Property(row=>row.PositionId).HasColumnName("support_request_position_id");b.Property(row=>row.VolunteerId).HasColumnName("volunteer_user_id");
        b.Property(row=>row.Status).HasColumnName("member_status").HasMaxLength(30).HasConversion(EnumText.Converter<SupportMemberStatus>());
        b.Property(row=>row.AssignmentId).HasColumnName("task_assignment_id");
        b.Property(row=>row.Version).HasColumnName("version").HasDefaultValue(1).IsConcurrencyToken();
        b.Property(row=>row.ReviewedBy).HasColumnName("reviewed_by_user_id");b.Property(row=>row.ReviewedAt).HasColumnName("reviewed_at");
        b.Property(row=>row.ReviewReason).HasColumnName("review_reason").HasMaxLength(500);
        b.HasOne<SupportPosition>().WithMany().HasForeignKey(row=>new {row.RequestId,row.PositionId})
            .HasPrincipalKey(row=>new {row.RequestId,row.Id}).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_support_member_position");
        b.HasOne<TaskAssignment>().WithMany().HasForeignKey(row=>row.AssignmentId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_support_member_assignment");
        b.HasIndex(row=>new {row.ResponseId,row.VolunteerId}).IsUnique().HasDatabaseName("ux_support_member_response_volunteer");
        b.HasIndex(row=>new {row.RequestId,row.VolunteerId}).IsUnique().HasFilter("member_status IN ('PENDING','APPROVED','COMPLETED')")
            .HasDatabaseName(ActiveVolunteerIndex);
        b.HasIndex(row=>row.AssignmentId).IsUnique().HasDatabaseName("ux_support_member_assignment");
        b.HasIndex(row=>new {row.RequestId,row.ResponseId}).HasDatabaseName("ix_support_member_response");
        b.HasIndex(row=>new {row.RequestId,row.PositionId}).HasDatabaseName("ix_support_member_position");
    }
}
