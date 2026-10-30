using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Npgsql;
using TaskExecution.Application.Assignments;
using TaskExecution.Domain;
using TaskExecution.Domain.Assignments;
using TaskExecution.Domain.Tasks;
using TaskExecution.Infrastructure.Data;
using TaskExecution.Infrastructure.Data.Configurations;

namespace TaskExecution.Infrastructure.Assignments;

public sealed class AssignmentStore(TaskExecutionDbContext db) : IAssignmentStore
{
    public async Task<T> InTaskTransactionAsync<T>(Guid taskId, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var key = $"task-mutation:{taskId:D}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key},0))", cancellationToken);
        try
        {
            var result = await action();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            var code = exception.Entries.Any(entry=>entry.Entity is TaskAssignment)
                ? "TASK_ASSIGNMENT_VERSION_MISMATCH" : "TASK_VERSION_MISMATCH";
            throw new DomainRuleException(code, "The task or assignment changed concurrently.", DomainErrorKind.PreconditionFailed);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new DomainRuleException("TASK_ASSIGNMENT_DUPLICATE", "The volunteer already has an active assignment or application.");
        }
    }

    public async Task<IReadOnlyList<TaskAssignment>> ListAsync(Guid taskId, CancellationToken cancellationToken) =>
        await db.Assignments.Where(row => row.TaskId == taskId)
            .OrderBy(row => row.AssignedAt ?? row.RequestedAt).ThenBy(row => row.Id).ToListAsync(cancellationToken);

    public Task<TaskAssignment?> GetAsync(Guid assignmentId, CancellationToken cancellationToken) =>
        db.Assignments.SingleOrDefaultAsync(row => row.Id == assignmentId, cancellationToken);

    public async Task<bool> HasScheduleConflictAsync(Guid volunteerId, Guid taskId, DateTimeOffset? startAt,
        DateTimeOffset deadline, CancellationToken cancellationToken)
    {
        if (!startAt.HasValue) return false;
        return await (from assignment in db.Assignments
                      join task in db.Tasks on assignment.TaskId equals task.Id
                      where assignment.VolunteerId == volunteerId && assignment.TaskId != taskId &&
                            (assignment.Status == AssignmentStatus.Accepted || assignment.Status == AssignmentStatus.InProgress ||
                             (assignment.Status == AssignmentStatus.Pending && assignment.Type == AssignmentType.Direct)) &&
                            task.StartAt.HasValue && task.StartAt < deadline && task.Deadline > startAt.Value
                      select assignment.Id).AnyAsync(cancellationToken);
    }

    public void Add(TaskAssignment assignment) => db.Assignments.Add(assignment);
    public Task SaveAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

public sealed class TaskAssignmentConfiguration : IEntityTypeConfiguration<TaskAssignment>
{
    public void Configure(EntityTypeBuilder<TaskAssignment> builder)
    {
        builder.ToTable("task_assignment");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasColumnName("task_assignment_id").ValueGeneratedNever();
        builder.Property(row => row.TaskId).HasColumnName("task_id");
        builder.Property(row => row.VolunteerId).HasColumnName("volunteer_user_id");
        builder.Property(row => row.Type).HasColumnName("assignment_type").HasConversion(EnumText.Converter<AssignmentType>()).HasMaxLength(40);
        builder.Property(row => row.Status).HasColumnName("assignment_status").HasConversion(EnumText.Converter<AssignmentStatus>()).HasMaxLength(30);
        builder.Property(row => row.Note).HasColumnName("assignment_note").HasMaxLength(2000);
        builder.Property(row => row.ResponseReason).HasColumnName("response_reason").HasMaxLength(500);
        builder.Property(row => row.RequestedAt).HasColumnName("requested_at");
        builder.Property(row => row.AssignedAt).HasColumnName("assigned_at");
        builder.Property(row => row.ApprovedAt).HasColumnName("approved_at");
        builder.Property(row => row.RejectedAt).HasColumnName("rejected_at");
        builder.Property(row => row.AssignedBy).HasColumnName("assigned_by_user_id");
        builder.Property(row => row.OverrideCapacity).HasColumnName("override_capacity");
        builder.Property(row => row.OverrideSkillMismatch).HasColumnName("override_skill_mismatch");
        builder.Property(row => row.OverrideReason).HasColumnName("override_reason").HasMaxLength(500);
        builder.Property(row => row.Version).HasColumnName("version").IsConcurrencyToken().HasDefaultValue(1);
        builder.Ignore(row => row.ReservesCapacity);
        builder.HasOne<WorkTask>().WithMany().HasForeignKey(row => row.TaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(row => new { row.TaskId, row.VolunteerId }).IsUnique()
            .HasFilter("assignment_status IN ('PENDING','ACCEPTED','IN_PROGRESS','COMPLETED')")
            .HasDatabaseName("ux_task_assignment_active_volunteer");
        builder.HasIndex(row => row.VolunteerId).HasDatabaseName("ix_task_assignment_volunteer");
    }
}
