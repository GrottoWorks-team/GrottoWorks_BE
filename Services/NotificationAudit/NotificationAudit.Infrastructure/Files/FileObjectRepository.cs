using NotificationAudit.Application.Files;
using NotificationAudit.Domain.Files;
using NotificationAudit.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace NotificationAudit.Infrastructure.Files;

internal sealed class FileObjectRepository(NotificationAuditDbContext dbContext) : IFileObjectRepository
{
    public void Add(FileObject file) => dbContext.FileObjects.Add(file);

    public async Task<IFileObjectTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        new FileObjectTransaction(await dbContext.Database.BeginTransactionAsync(cancellationToken));

    public Task<FileObject?> GetForUpdateAsync(Guid fileId, CancellationToken cancellationToken) =>
        dbContext.FileObjects
            .FromSqlInterpolated($"SELECT * FROM file_object WHERE file_id = {fileId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}

internal sealed class FileObjectTransaction(IDbContextTransaction transaction) : IFileObjectTransaction
{
    public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);
    public ValueTask DisposeAsync() => transaction.DisposeAsync();
}
