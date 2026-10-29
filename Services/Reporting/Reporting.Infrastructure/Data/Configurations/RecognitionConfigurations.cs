using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Reporting.Domain.Recognition;

namespace Reporting.Infrastructure.Data.Configurations;

/// <summary><c>contribution_type</c> (ERD v3.6.2) + the seeded system types.</summary>
public sealed class ContributionTypeConfiguration : IEntityTypeConfiguration<ContributionType>
{
    public const string CodeIndex = "ux_contribution_type_code";

    public void Configure(EntityTypeBuilder<ContributionType> builder)
    {
        builder.ToTable("contribution_type");
        builder.HasKey(type => type.ContributionTypeId);
        builder.Property(type => type.ContributionTypeId).HasColumnName("contribution_type_id").ValueGeneratedNever();
        builder.Property(type => type.Code).HasColumnName("contribution_code").HasMaxLength(ContributionType.MaxCodeLength).IsRequired();
        builder.Property(type => type.Name).HasColumnName("contribution_name").HasMaxLength(ContributionType.MaxNameLength).IsRequired();
        builder.Property(type => type.Description).HasColumnName("contribution_description");
        builder.Property(type => type.Status).HasColumnName("contribution_status").HasConversion(EnumText.Converter<ContributionTypeStatus>()).HasMaxLength(30);
        builder.Ignore(type => type.IsSystem);

        builder.HasIndex(type => type.Code).IsUnique().HasDatabaseName(CodeIndex);
        builder.HasData(ContributionType.SystemTypes.Select(type => ContributionType.System(type.Id, type.Code, type.Name, type.Description)));
    }
}

/// <summary><c>recognition_rule</c> (ERD v3.6.2): one row per version; (name, version) unique.</summary>
public sealed class RecognitionRuleConfiguration : IEntityTypeConfiguration<RecognitionRule>
{
    public const string NameVersionIndex = "ux_recognition_rule_name_version";

    public void Configure(EntityTypeBuilder<RecognitionRule> builder)
    {
        builder.ToTable("recognition_rule", table =>
            table.HasCheckConstraint("recognition_rule_valid_period", "\"effective_to\" IS NULL OR \"effective_to\" >= \"effective_from\""));
        builder.HasKey(rule => rule.RecognitionRuleId);
        builder.Property(rule => rule.RecognitionRuleId).HasColumnName("recognition_rule_id").ValueGeneratedNever();
        builder.Property(rule => rule.ContributionTypeId).HasColumnName("contribution_type_id").IsRequired();
        builder.Property(rule => rule.Name).HasColumnName("rule_name").HasMaxLength(RecognitionRule.MaxNameLength).IsRequired();
        builder.Property(rule => rule.RuleVersion).HasColumnName("rule_version").HasDefaultValue(1).IsRequired();
        builder.Property(rule => rule.Points).HasColumnName("rule_points").IsRequired();
        builder.Property(rule => rule.Description).HasColumnName("rule_description");
        builder.Property(rule => rule.Status).HasColumnName("rule_status").HasConversion(EnumText.Converter<RecognitionRuleStatus>()).HasMaxLength(30);
        builder.Property(rule => rule.EffectiveFrom).HasColumnName("effective_from").IsRequired();
        builder.Property(rule => rule.EffectiveTo).HasColumnName("effective_to");
        builder.Property(rule => rule.Version).IsRowVersion();

        builder.HasIndex(rule => new { rule.Name, rule.RuleVersion }).IsUnique().HasDatabaseName(NameVersionIndex);
        builder.HasIndex(rule => rule.ContributionTypeId, "ix_recognition_rule_contribution_type");
        builder.HasOne<ContributionType>().WithMany().HasForeignKey(rule => rule.ContributionTypeId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// <c>contribution</c> (ERD v3.6.2) + technical columns <c>parish_id</c>, <c>work_area_id</c>, <c>donor_user_id</c>;
/// one row per donation, one per (reference, user) otherwise (BR-55).
/// </summary>
public sealed class ContributionConfiguration : IEntityTypeConfiguration<Contribution>
{
    public const string DonationIndex = "ux_contribution_donation";
    public const string ReferenceIndex = "ux_contribution_reference_user";

    public void Configure(EntityTypeBuilder<Contribution> builder)
    {
        builder.ToTable("contribution", table => table.HasCheckConstraint(
            "contribution_single_source",
            "(\"donation_id\" IS NOT NULL AND \"contributed_by_user_id\" IS NULL AND \"reference_type\" IS NULL AND \"reference_id\" IS NULL) OR " +
            "(\"donation_id\" IS NULL AND \"contributed_by_user_id\" IS NOT NULL)"));
        builder.HasKey(contribution => contribution.ContributionId);
        builder.Property(contribution => contribution.ContributionId).HasColumnName("contribution_id").ValueGeneratedNever();
        // user, donation, season, parish and work area ids reference other services: no FK (spec 2.1).
        builder.Property(contribution => contribution.ContributedByUserId).HasColumnName("contributed_by_user_id");
        builder.Property(contribution => contribution.DonationId).HasColumnName("donation_id");
        builder.Property(contribution => contribution.SeasonId).HasColumnName("season_id").IsRequired();
        builder.Property(contribution => contribution.ParishId).HasColumnName("parish_id").IsRequired();
        builder.Property(contribution => contribution.WorkAreaId).HasColumnName("work_area_id");
        builder.Property(contribution => contribution.ContributionTypeId).HasColumnName("contribution_type_id").IsRequired();
        builder.Property(contribution => contribution.Description).HasColumnName("contribution_description");
        builder.Property(contribution => contribution.ContributionDate).HasColumnName("contribution_date").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(contribution => contribution.ReferenceType).HasColumnName("reference_type").HasMaxLength(50);
        builder.Property(contribution => contribution.ReferenceId).HasColumnName("reference_id");
        builder.Property(contribution => contribution.DonorUserId).HasColumnName("donor_user_id");
        builder.Ignore(contribution => contribution.CreditedUserId);

        builder.HasIndex(contribution => contribution.DonationId).IsUnique().HasDatabaseName(DonationIndex);
        builder.HasIndex(contribution => new { contribution.ReferenceType, contribution.ReferenceId, contribution.ContributedByUserId })
            .IsUnique().HasDatabaseName(ReferenceIndex).HasFilter("\"donation_id\" IS NULL");
        builder.HasIndex(contribution => new { contribution.ParishId, contribution.SeasonId }, "ix_contribution_parish_season");
        builder.HasIndex(contribution => contribution.ContributedByUserId, "ix_contribution_contributed_by");
        builder.HasIndex(contribution => contribution.DonorUserId, "ix_contribution_donor_user");
        builder.HasOne<ContributionType>().WithMany().HasForeignKey(contribution => contribution.ContributionTypeId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary><c>recognition_point</c> (ERD v3.6.2): (contribution, rule, user) unique — BR-55 / BR-68.</summary>
public sealed class RecognitionPointConfiguration : IEntityTypeConfiguration<RecognitionPoint>
{
    public const string UniqueIndex = "ux_recognition_point_contribution_rule_user";

    public void Configure(EntityTypeBuilder<RecognitionPoint> builder)
    {
        builder.ToTable("recognition_point");
        builder.HasKey(point => point.RecognitionPointId);
        builder.Property(point => point.RecognitionPointId).HasColumnName("recognition_point_id").ValueGeneratedNever();
        builder.Property(point => point.AwardedToUserId).HasColumnName("awarded_to_user_id").IsRequired();
        builder.Property(point => point.SeasonId).HasColumnName("season_id").IsRequired();
        builder.Property(point => point.RecognitionRuleId).HasColumnName("recognition_rule_id").IsRequired();
        builder.Property(point => point.ContributionId).HasColumnName("contribution_id").IsRequired();
        builder.Property(point => point.AwardedPoints).HasColumnName("awarded_points").IsRequired();
        builder.Property(point => point.Reason).HasColumnName("reason");
        builder.Property(point => point.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();

        builder.HasIndex(point => new { point.ContributionId, point.RecognitionRuleId, point.AwardedToUserId }).IsUnique().HasDatabaseName(UniqueIndex);
        builder.HasIndex(point => new { point.AwardedToUserId, point.SeasonId }, "ix_recognition_point_user_season");
        builder.HasOne<RecognitionRule>().WithMany().HasForeignKey(point => point.RecognitionRuleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Contribution>().WithMany().HasForeignKey(point => point.ContributionId).OnDelete(DeleteBehavior.Restrict);
    }
}
