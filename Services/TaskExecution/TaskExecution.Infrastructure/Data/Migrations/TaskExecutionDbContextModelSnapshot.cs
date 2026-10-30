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
        BuildAssignmentModel(modelBuilder);
        new TaskExecution.Infrastructure.Events.TaskEventOutboxConfiguration()
            .Configure(modelBuilder.Entity<TaskExecution.Infrastructure.Events.TaskEventOutboxMessage>());
    }

    private static void BuildAssignmentModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity("TaskExecution.Domain.Assignments.TaskAssignment", builder =>
        {
            builder.Property<Guid>("Id").HasColumnName("task_assignment_id").HasColumnType("uuid");
            builder.Property<Guid>("TaskId").HasColumnName("task_id").HasColumnType("uuid");
            builder.Property<Guid>("VolunteerId").HasColumnName("volunteer_user_id").HasColumnType("uuid");
            builder.Property<string>("Type").IsRequired().HasMaxLength(40).HasColumnName("assignment_type").HasColumnType("character varying(40)");
            builder.Property<string>("Status").IsRequired().HasMaxLength(30).HasColumnName("assignment_status").HasColumnType("character varying(30)");
            builder.Property<string>("Note").HasMaxLength(2000).HasColumnName("assignment_note").HasColumnType("character varying(2000)");
            builder.Property<string>("OverrideReason").HasMaxLength(500).HasColumnName("override_reason").HasColumnType("character varying(500)");
            builder.Property<bool>("OverrideCapacity").HasColumnName("override_capacity").HasColumnType("boolean");
            builder.Property<bool>("OverrideSkillMismatch").HasColumnName("override_skill_mismatch").HasColumnType("boolean");
            builder.Property<Guid?>("AssignedBy").HasColumnName("assigned_by_user_id").HasColumnType("uuid");
            builder.Property<DateTimeOffset?>("RequestedAt").HasColumnName("requested_at").HasColumnType("timestamp with time zone");
            builder.Property<DateTimeOffset?>("AssignedAt").HasColumnName("assigned_at").HasColumnType("timestamp with time zone");
            builder.Property<DateTimeOffset?>("ApprovedAt").HasColumnName("approved_at").HasColumnType("timestamp with time zone");
            builder.Property<DateTimeOffset?>("RejectedAt").HasColumnName("rejected_at").HasColumnType("timestamp with time zone");
            builder.Property<int>("Version").IsConcurrencyToken().ValueGeneratedOnAdd().HasDefaultValue(1).HasColumnName("version").HasColumnType("integer");
            builder.HasKey("Id");
            builder.HasIndex("TaskId","VolunteerId").IsUnique().HasFilter("assignment_status IN ('PENDING','ACCEPTED','IN_PROGRESS','COMPLETED')").HasDatabaseName("ux_task_assignment_active_volunteer");
            builder.HasIndex("VolunteerId").HasDatabaseName("ix_task_assignment_volunteer");
            builder.ToTable("task_assignment");
            builder.HasOne("TaskExecution.Domain.Tasks.WorkTask", null).WithMany().HasForeignKey("TaskId").OnDelete(DeleteBehavior.Restrict).IsRequired();
        });
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
            builder.Property<string>("CompletionCriteria").HasMaxLength(4000).HasColumnType("character varying(4000)").HasColumnName("completion_criteria");
            builder.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone").HasColumnName("created_at");
            builder.Property<Guid>("CreatedByUserId").HasColumnType("uuid").HasColumnName("created_by_user_id");
            builder.Property<DateTimeOffset>("Deadline").HasColumnType("timestamp with time zone").HasColumnName("deadline");
            builder.Property<string>("Description").HasMaxLength(2000).HasColumnType("character varying(2000)").HasColumnName("task_description");
            builder.Property<int>("EstimatedMinutes").HasColumnType("integer").HasColumnName("estimated_minutes");
            builder.Property<string>("Name").IsRequired().HasMaxLength(160).HasColumnType("character varying(160)").HasColumnName("task_name");
            builder.Property<string>("Priority").IsRequired().HasMaxLength(30).HasColumnType("character varying(30)").HasColumnName("task_priority");
            builder.Property<Guid?>("PurchaseRequestId").HasColumnType("uuid").HasColumnName("purchase_request_id");
            builder.Property<int>("RequiredVolunteerCount").HasColumnType("integer").HasColumnName("required_volunteers");
            builder.Property<DateTimeOffset?>("StartAt").HasColumnType("timestamp with time zone").HasColumnName("start_at");
            builder.Property<string>("Status").IsRequired().HasMaxLength(30).HasColumnType("character varying(30)").HasColumnName("task_status");
            builder.Property<string>("Type").IsRequired().HasMaxLength(30).HasColumnType("character varying(30)").HasColumnName("task_type");
            builder.Property<DateTimeOffset?>("UpdatedAt").HasColumnType("timestamp with time zone").HasColumnName("updated_at");
            builder.Property<int>("Version").IsConcurrencyToken().ValueGeneratedOnAdd().HasColumnType("integer").HasDefaultValue(1).HasColumnName("version");
            builder.Property<Guid>("WorkAreaId").HasColumnType("uuid").HasColumnName("work_area_id");
            builder.HasKey("Id");
            builder.HasIndex("PurchaseRequestId").HasDatabaseName("ix_task_purchase_request_id");
            builder.HasIndex("WorkAreaId").HasDatabaseName("ix_task_work_area_id");
            builder.HasIndex("WorkAreaId", "Status").HasDatabaseName("ix_task_work_area_status");
            builder.ToTable("task", null, table =>
            {
                table.HasCheckConstraint("ck_task_positive_estimate", "estimated_minutes > 0");
                table.HasCheckConstraint("ck_task_positive_volunteer_count", "required_volunteers > 0");
                table.HasCheckConstraint(
                    "ck_task_purchase_link",
                    "(task_type = 'PURCHASE' AND purchase_request_id IS NOT NULL) OR (task_type <> 'PURCHASE' AND purchase_request_id IS NULL)");
            });
        });

        modelBuilder.Entity("TaskExecution.Domain.Tasks.TaskSkill", builder =>
        {
            builder.Property<Guid>("Id").HasColumnType("uuid").HasColumnName("task_skill_id");
            builder.Property<int?>("RequiredVolunteerCount").HasColumnType("integer").HasColumnName("required_volunteer_count");
            builder.Property<string>("RequiredSkillLevel").HasMaxLength(30)
                .HasColumnType("character varying(30)").HasColumnName("required_skill_level");
            builder.Property<Guid>("SkillId").HasColumnType("uuid").HasColumnName("skill_id");
            builder.Property<Guid>("TaskId").HasColumnType("uuid").HasColumnName("task_id");
            builder.HasKey("Id");
            builder.HasIndex("TaskId", "SkillId").IsUnique()
                .HasDatabaseName("ux_task_skill_task_id_skill_id");
            builder.ToTable("task_skill", (string)null);
        });

        modelBuilder.Entity("TaskExecution.Domain.Tasks.TaskSkill", builder =>
        {
            builder.HasOne("TaskExecution.Domain.Tasks.WorkTask", null)
                .WithMany("Skills")
                .HasForeignKey("TaskId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });

        modelBuilder.Entity("TaskExecution.Domain.Tasks.WorkTask", builder =>
        {
            builder.Navigation("Skills");
        });
    }
}
