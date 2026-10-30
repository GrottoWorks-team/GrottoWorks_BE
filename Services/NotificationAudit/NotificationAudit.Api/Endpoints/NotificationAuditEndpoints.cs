using BuildingBlocks.Web;
using Microsoft.AspNetCore.Mvc;
using NotificationAudit.Application.Files;
using NotificationAudit.Contracts.Files;
using NotificationAudit.Api.Common;

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
}
