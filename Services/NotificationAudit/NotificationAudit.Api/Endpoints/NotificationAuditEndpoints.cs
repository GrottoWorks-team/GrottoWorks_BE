using BuildingBlocks.Web;
using Microsoft.AspNetCore.Mvc;
using NotificationAudit.Application.Files;
using NotificationAudit.Contracts.Files;
using NotificationAudit.Api.Common;
using NotificationAudit.Application.Notifications;
using NotificationAudit.Contracts.Notifications;
using BuildingBlocks.Security;

namespace NotificationAudit.Api.Endpoints;

public static class NotificationAuditEndpoints
{
    public static void MapNotificationAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/files/upload-requests", CreateUploadRequestAsync)
            .WithName("createUploadRequest")
            .WithTags("Files")
            .RequireAuthorization()
            .Produces<ApiResponse<UploadRequestDto>>(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)
            .Produces<ProblemDetails>(StatusCodes.Status503ServiceUnavailable);

        app.MapPost("/api/v1/files/{fileId:guid}/complete", CompleteUploadAsync)
            .WithName("completeUpload")
            .WithTags("Files")
            .RequireAuthorization()
            .Produces<ApiResponse<FileReferenceDto>>()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .Produces<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)
            .Produces<ProblemDetails>(StatusCodes.Status503ServiceUnavailable);

        app.MapGet("/api/v1/files/{fileId:guid}/download-url", GetDownloadUrlAsync)
            .WithName("getDownloadUrl")
            .WithTags("Files")
            .RequireAuthorization()
            .Produces<ApiResponse<FileDownloadDto>>()
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .Produces<ProblemDetails>(StatusCodes.Status503ServiceUnavailable);

        app.MapDelete("/api/v1/files/{fileId:guid}", DeleteFileAsync)
            .WithName("deleteUnattachedFile")
            .WithTags("Files")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .Produces<ProblemDetails>(StatusCodes.Status503ServiceUnavailable);

        app.MapGet("/internal/files/{fileId:guid}", GetInternalFileAsync)
            .WithName("getInternalFile")
            .ExcludeFromDescription()
            .AddEndpointFilter<InternalApiKeyFilter>()
            .Produces<ApiResponse<InternalFileDto>>()
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        app.MapGet("/api/v1/notifications", ListNotificationsAsync)
            .WithName("listNotifications").WithTags("Notifications").RequireAuthorization()
            .Produces<PagedResponse<NotificationDto>>().Produces<ProblemDetails>(400).Produces<ProblemDetails>(401);
        app.MapPost("/api/v1/notifications/{notificationId:guid}/read", MarkNotificationReadAsync)
            .WithName("markNotificationRead").WithTags("Notifications").RequireAuthorization()
            .Produces(204).Produces<ProblemDetails>(401).Produces<ProblemDetails>(404);
        app.MapPost("/api/v1/notifications/read-all", MarkAllNotificationsReadAsync)
            .WithName("markAllNotificationsRead").WithTags("Notifications").RequireAuthorization()
            .Produces(204).Produces<ProblemDetails>(401);

        app.MapGet("/api/v1/users/me/notification-preferences", GetNotificationPreferencesAsync)
            .WithName("getNotificationPreferences").WithTags("Notifications").RequireAuthorization()
            .Produces<ApiResponse<NotificationPreferenceDto>>().Produces<ProblemDetails>(401);
        app.MapPut("/api/v1/users/me/notification-preferences", UpdateNotificationPreferencesAsync)
            .WithName("updateNotificationPreferences").WithTags("Notifications").RequireAuthorization()
            .Produces<ApiResponse<NotificationPreferenceDto>>().Produces<ProblemDetails>(400).Produces<ProblemDetails>(401).Produces<ProblemDetails>(422);

        app.MapPatch("/api/v1/notification-rules/{ruleId:guid}", UpdateNotificationRuleAsync)
            .WithName("updateNotificationRule").WithTags("Notifications")
            .RequireAuthorization(policy => policy.RequireRole(GrottoWorksRoles.Admin))
            .Produces<ApiResponse<NotificationRuleDto>>().Produces<ProblemDetails>(400).Produces<ProblemDetails>(401)
            .Produces<ProblemDetails>(403).Produces<ProblemDetails>(404).Produces<ProblemDetails>(412).Produces<ProblemDetails>(422);
    }

    private static async Task<IResult> CreateUploadRequestAsync(
        CreateUploadRequest request,
        HttpContext httpContext,
        CreateUploadRequestHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        return Results.Created($"/api/v1/files/{result.FileId}", ApiResults.Ok(result, httpContext));
    }

    private static async Task<IResult> CompleteUploadAsync(
        Guid fileId,
        CompleteUploadRequest request,
        HttpContext httpContext,
        CompleteUploadHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(fileId, request, cancellationToken);
        return Results.Ok(ApiResults.Ok(result, httpContext));
    }

    private static async Task<IResult> GetDownloadUrlAsync(
        Guid fileId,
        HttpContext httpContext,
        GetFileDownloadHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(fileId, cancellationToken);
        return Results.Ok(ApiResults.Ok(result, httpContext));
    }

    private static async Task<IResult> DeleteFileAsync(
        Guid fileId,
        DeleteUnattachedFileHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(fileId, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetInternalFileAsync(
        Guid fileId,
        HttpContext httpContext,
        GetInternalFileHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(fileId, cancellationToken);
        return Results.Ok(ApiResults.Ok(result, httpContext));
    }

    private static async Task<IResult> ListNotificationsAsync(
        bool? isRead,
        string? eventType,
        int? page,
        int? size,
        HttpContext httpContext,
        ListNotificationsHandler handler,
        CancellationToken cancellationToken)
    {
        var pageNumber = Math.Max(page ?? 1, 1);
        var pageSize = Math.Clamp(size ?? 20, 1, 100);
        var result = await handler.HandleAsync(isRead, eventType, pageNumber, pageSize, cancellationToken);
        return Results.Ok(ApiResults.Page(result.Items, result.Total, pageNumber, pageSize, httpContext));
    }

    private static async Task<IResult> MarkNotificationReadAsync(
        Guid notificationId,
        MarkNotificationReadHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(notificationId, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> MarkAllNotificationsReadAsync(
        MarkAllNotificationsReadHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetNotificationPreferencesAsync(
        HttpContext httpContext,
        GetNotificationPreferenceHandler handler,
        CancellationToken cancellationToken) =>
        Results.Ok(ApiResults.Ok(await handler.HandleAsync(cancellationToken), httpContext));

    private static async Task<IResult> UpdateNotificationPreferencesAsync(
        UpdateNotificationPreferenceRequest request,
        HttpContext httpContext,
        UpdateNotificationPreferenceHandler handler,
        CancellationToken cancellationToken) =>
        Results.Ok(ApiResults.Ok(await handler.HandleAsync(request, cancellationToken), httpContext));

    private static async Task<IResult> UpdateNotificationRuleAsync(
        Guid ruleId,
        UpdateNotificationRuleRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        HttpContext httpContext,
        UpdateNotificationRuleHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryParseVersion(ifMatch, out var expectedVersion))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Invalid If-Match header",
                detail: "If-Match must contain a positive integer version.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "INVALID_IF_MATCH",
                    ["correlationId"] = BuildingBlocks.Web.CorrelationIdMiddlewareExtensions.GetCorrelationId(httpContext),
                    ["traceId"] = httpContext.TraceIdentifier
                });
        }

        var result = await handler.HandleAsync(ruleId, request.InAppEnabled!.Value, expectedVersion, cancellationToken);
        httpContext.Response.Headers.ETag = $"\"{result.Version}\"";
        return Results.Ok(ApiResults.Ok(result, httpContext));
    }

    private static bool TryParseVersion(string? value, out int? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        var normalized = value.Trim().Trim('"');
        if (!int.TryParse(normalized, out var parsed) || parsed <= 0) return false;
        version = parsed;
        return true;
    }
}
