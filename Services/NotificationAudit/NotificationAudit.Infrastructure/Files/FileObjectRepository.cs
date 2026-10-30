using NotificationAudit.Application.Files;
using NotificationAudit.Domain.Files;
using NotificationAudit.Infrastructure.Data;

namespace NotificationAudit.Infrastructure.Files;

internal sealed class FileObjectRepository(NotificationAuditDbContext dbContext) : IFileObjectRepository
{
    public void Add(FileObject file) => dbContext.FileObjects.Add(file);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
