using BuildingBlocks.Web;
using Microsoft.AspNetCore.Mvc;
using NotificationAudit.Application.Files;
using NotificationAudit.Contracts.Files;

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
}
