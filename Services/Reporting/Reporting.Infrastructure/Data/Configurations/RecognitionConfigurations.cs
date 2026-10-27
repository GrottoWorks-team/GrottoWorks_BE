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
