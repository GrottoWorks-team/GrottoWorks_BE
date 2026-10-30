using BuildingBlocks.Web;
using TaskExecution.Application.Events;

namespace TaskExecution.Api.Common;

public sealed class TaskCorrelationContext(IHttpContextAccessor accessor) : ITaskCorrelationContext
{
    public string CorrelationId => accessor.HttpContext is { } context
        ? CorrelationIdMiddlewareExtensions.GetCorrelationId(context)
        : Guid.NewGuid().ToString();
}
