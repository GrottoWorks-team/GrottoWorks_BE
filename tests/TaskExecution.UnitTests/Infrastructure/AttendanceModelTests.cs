using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TaskExecution.Domain.Attendance;
using TaskExecution.Infrastructure.Data;

namespace TaskExecution.UnitTests.Infrastructure;

public sealed class AttendanceModelTests
{
    [Fact]
    public void Request_and_session_have_concurrency_history_and_unique_open_indexes()
    {
        using var db=new TaskExecutionDbContext(new DbContextOptionsBuilder<TaskExecutionDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=test;Password=test").Options);
        var request=db.Model.FindEntityType(typeof(AttendanceRequest))!;
        var session=db.Model.FindEntityType(typeof(AttendanceSession))!;
        request.FindProperty(nameof(AttendanceRequest.Version))!.IsConcurrencyToken.Should().BeTrue();
        request.GetIndexes().Should().Contain(i=>i.IsUnique && i.GetFilter()=="request_status = 'PENDING'");
        session.GetIndexes().Should().Contain(i=>i.IsUnique && i.GetFilter()=="check_out_at IS NULL");
        session.GetForeignKeys().Should().HaveCount(2);
        db.Database.HasPendingModelChanges().Should().BeFalse();
    }
}
