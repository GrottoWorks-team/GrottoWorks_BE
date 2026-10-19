using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TaskExecution.Infrastructure.Data.Migrations;

[DbContext(typeof(TaskExecutionDbContext))]
public partial class TaskExecutionDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder) => BuildCurrentModel(modelBuilder);

    internal static void BuildScopeModel(ModelBuilder modelBuilder) => BuildScopeModelCore(modelBuilder);

    internal static void BuildCurrentModel(ModelBuilder modelBuilder)
    {
        BuildScopeModelCore(modelBuilder);
        BuildTaskModel(modelBuilder);
    }

    private static void BuildScopeModelCore(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder.HasAnnotation("ProductVersion", "10.0.4")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);
        NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

        modelBuilder.Entity("TaskExecution.Domain.Scopes.SeasonScope", builder =>
        {
            builder.Property<Guid>("SeasonId").HasColumnType("uuid").HasColumnName("season_id");
            builder.Property<DateOnly>("EndDate").HasColumnType("date").HasColumnName("end_date");
            builder.Property<Guid>("ParishId").HasColumnType("uuid").HasColumnName("parish_id");
            builder.Property<long>("SourceVersion").HasColumnType("bigint").HasColumnName("source_version");
            builder.Property<DateOnly>("StartDate").HasColumnType("date").HasColumnName("start_date");
            builder.Property<string>("Status").IsRequired().HasMaxLength(30)
                .HasColumnType("character varying(30)").HasColumnName("season_status");
            builder.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone").HasColumnName("updated_at");
            builder.HasKey("SeasonId");
            builder.HasIndex("ParishId").HasDatabaseName("ix_season_scope_parish_id");
            builder.ToTable("season_scope", (string)null);
        });

        modelBuilder.Entity("TaskExecution.Domain.Scopes.WorkAreaScope", builder =>
        {
            builder.Property<Guid>("WorkAreaId").HasColumnType("uuid").HasColumnName("work_area_id");
            builder.Property<Guid?>("CommunityId").HasColumnType("uuid").HasColumnName("community_id");
            builder.Property<DateOnly?>("Deadline").HasColumnType("date").HasColumnName("deadline");
            builder.Property<Guid?>("LeaderUserId").HasColumnType("uuid").HasColumnName("leader_user_id");
            builder.Property<Guid?>("MaterialOfficerUserId").HasColumnType("uuid").HasColumnName("material_officer_user_id");
            builder.Property<string>("Name").IsRequired().HasMaxLength(150)
                .HasColumnType("character varying(150)").HasColumnName("work_area_name");
            builder.Property<Guid>("ParishId").HasColumnType("uuid").HasColumnName("parish_id");
            builder.Property<Guid>("SeasonId").HasColumnType("uuid").HasColumnName("season_id");
            builder.Property<long>("SourceVersion").HasColumnType("bigint").HasColumnName("source_version");
            builder.Property<DateOnly?>("StartDate").HasColumnType("date").HasColumnName("start_date");
            builder.Property<string>("Status").IsRequired().HasMaxLength(30)
                .HasColumnType("character varying(30)").HasColumnName("work_area_status");
            builder.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone").HasColumnName("updated_at");
            builder.HasKey("WorkAreaId");
            builder.HasIndex("LeaderUserId").HasDatabaseName("ix_work_area_scope_leader_user_id");
            builder.HasIndex("ParishId").HasDatabaseName("ix_work_area_scope_parish_id");
            builder.HasIndex("SeasonId").HasDatabaseName("ix_work_area_scope_season_id");
            builder.ToTable("work_area_scope", (string)null);
        });
#pragma warning restore 612, 618
    }

    private static void BuildTaskModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity("TaskExecution.Domain.Tasks.WorkTask", builder =>
        {
            builder.Property<Guid>("Id").HasColumnType("uuid").HasColumnName("task_id");
            builder.Property<string>("CompletionCriteria").HasColumnType("text").HasColumnName("completion_criteria");
            builder.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone").HasColumnName("created_at");
            builder.Property<Guid>("CreatedByUserId").HasColumnType("uuid").HasColumnName("created_by_user_id");
            builder.Property<DateTimeOffset?>("Deadline").HasColumnType("timestamp with time zone").HasColumnName("deadline");
            builder.Property<string>("Description").HasColumnType("text").HasColumnName("task_description");
            builder.Property<decimal?>("EstimatedHours").HasPrecision(6, 2).HasColumnType("numeric(6,2)").HasColumnName("estimated_hours");
            builder.Property<string>("Name").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)").HasColumnName("task_name");
            builder.Property<string>("Priority").IsRequired().HasMaxLength(30).HasColumnType("character varying(30)").HasColumnName("task_priority");
            builder.Property<Guid?>("PurchaseRequestId").HasColumnType("uuid").HasColumnName("purchase_request_id");
            builder.Property<int>("RequiredVolunteerCount").HasColumnType("integer").HasColumnName("required_volunteer_count");
            builder.Property<DateTimeOffset?>("StartAt").HasColumnType("timestamp with time zone").HasColumnName("start_date");
            builder.Property<string>("Status").IsRequired().HasMaxLength(30).HasColumnType("character varying(30)").HasColumnName("task_status");
            builder.Property<string>("Type").IsRequired().HasMaxLength(30).HasColumnType("character varying(30)").HasColumnName("task_type");
            builder.Property<DateTimeOffset?>("UpdatedAt").HasColumnType("timestamp with time zone").HasColumnName("updated_at");
            builder.Property<int>("Version").IsConcurrencyToken().ValueGeneratedOnAdd().HasColumnType("integer").HasDefaultValue(1).HasColumnName("version");
            builder.Property<Guid>("WorkAreaId").HasColumnType("uuid").HasColumnName("work_area_id");
            builder.HasKey("Id");
            builder.HasIndex("PurchaseRequestId").HasDatabaseName("ix_task_purchase_request_id");
            builder.HasIndex("WorkAreaId").HasDatabaseName("ix_task_work_area_id");
            builder.HasIndex("WorkAreaId", "Status").HasDatabaseName("ix_task_work_area_status");
            builder.ToTable("task", null, table => table.HasCheckConstraint(
                "ck_task_purchase_link",
                "(task_type = 'PURCHASE' AND purchase_request_id IS NOT NULL) OR (task_type <> 'PURCHASE' AND purchase_request_id IS NULL)"));
        });
    }
}
