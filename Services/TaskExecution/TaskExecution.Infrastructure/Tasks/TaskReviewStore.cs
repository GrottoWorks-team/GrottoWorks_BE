using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskExecution.Application.Tasks;
using TaskExecution.Domain.Tasks;
using TaskExecution.Infrastructure.Data;

namespace TaskExecution.Infrastructure.Tasks;

public sealed class FileAttachmentIntent
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public Guid ProgressId { get; set; }
    public Guid FileId { get; set; }
    public Guid UploaderId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Outcome { get; set; }
    public AttachmentIntent ToIntent()=>new(Id,TaskId,ProgressId,FileId,UploaderId,CreatedAt);
}
public sealed class TaskReviewStore(TaskExecutionDbContext db) : ITaskReviewStore
{
    public async Task<(IReadOnlyList<TaskProgressDto> Items,long Total)> GetPageAsync(Guid taskId,int page,int size,bool ascending,CancellationToken ct)
    {
        var query=db.Set<TaskProgress>().AsNoTracking().Where(row=>row.TaskId==taskId);
        var total=await query.LongCountAsync(ct);
        var ordered=ascending ? query.OrderBy(row=>row.SubmittedAt).ThenBy(row=>row.Id)
            : query.OrderByDescending(row=>row.SubmittedAt).ThenByDescending(row=>row.Id);
        var rows=await ordered.Skip((page-1)*size).Take(size).ToListAsync(ct);
        var ids=rows.Select(row=>row.Id).ToArray();
        var evidence=await db.Set<ProgressEvidence>().AsNoTracking().Where(row=>ids.Contains(row.ProgressId)).ToListAsync(ct);
        var byProgress=evidence.ToLookup(row=>row.ProgressId);
        return (rows.Select(row=>new TaskProgressDto(row.Id,row.TaskId,row.SubmittedByUserId,row.PercentComplete,row.Note,row.SubmittedAt,
            row.IsSubmission,row.ApprovedByUserId,row.ApprovedAt,byProgress[row.Id].ToArray())).ToArray(),total);
    }
    public async Task<IReadOnlyList<TaskProgressDto>> ListAsync(Guid taskId,CancellationToken ct)
    {
        var rows=await db.Set<TaskProgress>().AsNoTracking().Where(row=>row.TaskId==taskId)
            .OrderByDescending(row=>row.SubmittedAt).ThenByDescending(row=>row.Id).ToListAsync(ct);
        var ids=rows.Select(row=>row.Id).ToArray();
        var evidence=await db.Set<ProgressEvidence>().AsNoTracking().Where(row=>ids.Contains(row.ProgressId)).ToListAsync(ct);
        return rows.Select(row=>new TaskProgressDto(row.Id,row.TaskId,row.SubmittedByUserId,row.PercentComplete,row.Note,row.SubmittedAt,
            row.IsSubmission,row.ApprovedByUserId,row.ApprovedAt,evidence.Where(e=>e.ProgressId==row.Id).ToArray())).ToArray();
    }
    public async Task<IReadOnlyList<TaskCompletionReview>> ReviewsAsync(Guid taskId,CancellationToken ct)=>
        await db.Set<TaskCompletionReview>().AsNoTracking().Where(row=>row.TaskId==taskId).OrderBy(row=>row.ReviewedAt).ToListAsync(ct);
    public async Task<decimal> ApprovedHoursAsync(Guid taskId,CancellationToken ct)=>
        (await (from attendance in db.AttendanceSessions join assignment in db.Assignments on attendance.AssignmentId equals assignment.Id
        where assignment.TaskId==taskId select (int?)attendance.ApprovedMinutes).SumAsync(ct) ?? 0)/60m;
    public async Task PrepareAttachmentsAsync(IReadOnlyList<AttachmentIntent> intents,CancellationToken ct)
    {
        foreach(var row in intents) db.Set<FileAttachmentIntent>().Add(new() { Id=row.Id,TaskId=row.TaskId,ProgressId=row.ProgressId,
            FileId=row.FileId,UploaderId=row.UploaderId,CreatedAt=row.CreatedAt,NextAttemptAt=row.CreatedAt.AddMinutes(5) });
        if(intents.Count>0) await db.SaveChangesAsync(ct);
    }
    public async Task EnsureAttachmentsActiveAsync(IReadOnlyList<AttachmentIntent> intents,CancellationToken ct)
    {
        var ids=intents.Select(row=>row.Id).ToArray();
        if(await db.Set<FileAttachmentIntent>().AsNoTracking().CountAsync(row=>ids.Contains(row.Id) && row.CompletedAt==null,ct)!=ids.Length)
            throw new TaskExecution.Domain.DomainRuleException("TASK_EVIDENCE_OPERATION_EXPIRED","The abandoned evidence operation was recovered; submit again.");
    }
    public async Task<AttachmentIntent?> FindAttachmentAsync(Guid progressId,Guid fileId,CancellationToken ct)
    {
        var row=await db.Set<FileAttachmentIntent>().AsNoTracking().SingleOrDefaultAsync(row=>row.ProgressId==progressId && row.FileId==fileId,ct);
        return row?.ToIntent();
    }
    public void Add(TaskProgress progress,IReadOnlyList<ProgressEvidence> evidence)
    {
        db.Set<TaskProgress>().Add(progress); db.Set<ProgressEvidence>().AddRange(evidence);
    }
    public void Add(TaskCompletionReview review)=>db.Set<TaskCompletionReview>().Add(review);
    public Task<TaskProgress?> LatestSubmissionAsync(Guid taskId,CancellationToken ct)=>db.Set<TaskProgress>()
        .Where(row=>row.TaskId==taskId && row.IsSubmission).OrderByDescending(row=>row.SubmittedAt).ThenByDescending(row=>row.Id).FirstOrDefaultAsync(ct);
}

public sealed class TaskProgressConfiguration : IEntityTypeConfiguration<TaskProgress>
{
    public void Configure(EntityTypeBuilder<TaskProgress> b)
    {
        b.ToTable("task_progress",t=>{
            t.HasCheckConstraint("ck_task_progress_percent","progress_percent BETWEEN 0 AND 100");
            t.HasCheckConstraint("ck_task_progress_approval","(approved_by_user_id IS NULL AND approved_at IS NULL) OR (approved_by_user_id IS NOT NULL AND approved_at IS NOT NULL AND progress_percent=100 AND approved_at>=submitted_at)");
        });
        b.HasKey(x=>x.Id); b.Property(x=>x.Id).HasColumnName("task_progress_id").ValueGeneratedNever();
        b.Property(x=>x.TaskId).HasColumnName("task_id"); b.Property(x=>x.SubmittedByUserId).HasColumnName("submitted_by_user_id");
        b.Property(x=>x.PercentComplete).HasColumnName("progress_percent"); b.Property(x=>x.Note).HasColumnName("progress_note").HasMaxLength(2000);
        b.Property(x=>x.SubmittedAt).HasColumnName("submitted_at"); b.Property(x=>x.IsSubmission).HasColumnName("is_submission");
        b.Property(x=>x.ApprovedByUserId).HasColumnName("approved_by_user_id"); b.Property(x=>x.ApprovedAt).HasColumnName("approved_at");
        b.HasOne<WorkTask>().WithMany().HasForeignKey(x=>x.TaskId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x=>new{x.TaskId,x.SubmittedAt}).HasDatabaseName("ix_task_progress_history");
    }
}
public sealed class ProgressEvidenceConfiguration : IEntityTypeConfiguration<ProgressEvidence>
{
    public void Configure(EntityTypeBuilder<ProgressEvidence> b)
    {
        b.ToTable("progress_evidence");b.HasKey(x=>x.Id);b.Property(x=>x.Id).HasColumnName("progress_evidence_id").ValueGeneratedNever();
        b.Property(x=>x.ProgressId).HasColumnName("task_progress_id");b.Property(x=>x.FileId).HasColumnName("file_id");
        b.Property(x=>x.Caption).HasColumnName("evidence_caption").HasMaxLength(255);b.Property(x=>x.UploadedAt).HasColumnName("uploaded_at");
        b.HasOne<TaskProgress>().WithMany().HasForeignKey(x=>x.ProgressId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x=>x.FileId).IsUnique().HasDatabaseName("progress_evidence_file_id_key");
    }
}
public sealed class TaskCompletionReviewConfiguration : IEntityTypeConfiguration<TaskCompletionReview>
{
    public void Configure(EntityTypeBuilder<TaskCompletionReview> b)
    {
        b.ToTable("task_completion_review");b.HasKey(x=>x.Id);b.Property(x=>x.Id).HasColumnName("review_id").ValueGeneratedNever();
        b.Property(x=>x.TaskId).HasColumnName("task_id");b.Property(x=>x.ProgressId).HasColumnName("task_progress_id");
        b.Property(x=>x.ReviewedByUserId).HasColumnName("reviewed_by_user_id");b.Property(x=>x.Approved).HasColumnName("approved");
        b.Property(x=>x.Reason).HasColumnName("reason").HasMaxLength(2000);b.Property(x=>x.ReviewedAt).HasColumnName("reviewed_at");
        b.HasOne<TaskProgress>().WithMany().HasForeignKey(x=>x.ProgressId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x=>x.ProgressId).IsUnique().HasDatabaseName("task_completion_review_task_progress_id_key");
    }
}
public sealed class FileAttachmentIntentConfiguration : IEntityTypeConfiguration<FileAttachmentIntent>
{
    public void Configure(EntityTypeBuilder<FileAttachmentIntent> b)
    {
        b.ToTable("task_file_attachment_intent");b.HasKey(x=>x.Id);b.Property(x=>x.Id).HasColumnName("intent_id").ValueGeneratedNever();
        b.Property(x=>x.TaskId).HasColumnName("task_id");b.Property(x=>x.ProgressId).HasColumnName("task_progress_id");b.Property(x=>x.FileId).HasColumnName("file_id");
        b.Property(x=>x.UploaderId).HasColumnName("uploader_id");b.Property(x=>x.CreatedAt).HasColumnName("created_at");
        b.Property(x=>x.NextAttemptAt).HasColumnName("next_attempt_at");b.Property(x=>x.Attempts).HasColumnName("attempts");
        b.Property(x=>x.CompletedAt).HasColumnName("completed_at");b.Property(x=>x.Outcome).HasColumnName("outcome").HasMaxLength(30);
        b.HasIndex(x=>x.NextAttemptAt).HasFilter("completed_at IS NULL").HasDatabaseName("ix_task_file_intents_pending");
    }
}
