using BuildingBlocks.Web;
using Microsoft.AspNetCore.Mvc;
using NotificationAudit.Api.Common;
using NotificationAudit.Application.Files;
using NotificationAudit.Contracts.Files;

namespace NotificationAudit.Api.Endpoints;

public static class FileAttachmentEndpoints
{
    public static void MapInternalFileAttachmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/files").ExcludeFromDescription().AddEndpointFilter<InternalApiKeyFilter>();
        group.MapPost("/{fileId:guid}/attachment-reservations", ReserveAsync)
            .WithName("reserveInternalFileAttachment").Produces<ApiResponse<FileAttachmentDto>>()
            .Produces<ProblemDetails>(401).Produces<ProblemDetails>(403).Produces<ProblemDetails>(404)
            .Produces<ProblemDetails>(409).Produces<ProblemDetails>(422);
        group.MapPost("/{fileId:guid}/attachment-reservations/finalize", FinalizeAsync)
            .WithName("finalizeInternalFileAttachment").Produces<ApiResponse<FileReferenceDto>>()
            .Produces<ProblemDetails>(401).Produces<ProblemDetails>(404).Produces<ProblemDetails>(409).Produces<ProblemDetails>(422);
        group.MapPost("/{fileId:guid}/attachment-reservations/release", ReleaseAsync)
            .WithName("releaseInternalFileAttachment").Produces(204)
            .Produces<ProblemDetails>(401).Produces<ProblemDetails>(404).Produces<ProblemDetails>(409).Produces<ProblemDetails>(422);
        group.MapPost("/{fileId:guid}/record-download-url", DownloadAsync)
            .WithName("getInternalRecordFileDownloadUrl").Produces<ApiResponse<FileDownloadDto>>()
            .Produces<ProblemDetails>(401).Produces<ProblemDetails>(404).Produces<ProblemDetails>(422).Produces<ProblemDetails>(503);
    }

    private static async Task<IResult> ReserveAsync(Guid fileId, ReserveFileAttachmentRequest request,
        FileAttachmentHandler handler, HttpContext context, CancellationToken cancellationToken) =>
        Results.Ok(ApiResults.Ok(await handler.ReserveAsync(fileId, request, cancellationToken), context));

    private static async Task<IResult> FinalizeAsync(Guid fileId, FileAttachmentCommand request,
        FileAttachmentHandler handler, HttpContext context, CancellationToken cancellationToken) =>
        Results.Ok(ApiResults.Ok(await handler.FinalizeAsync(fileId, request, cancellationToken), context));

    private static async Task<IResult> ReleaseAsync(Guid fileId, FileAttachmentCommand request,
        FileAttachmentHandler handler, CancellationToken cancellationToken)
    {
        await handler.ReleaseAsync(fileId, request, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> DownloadAsync(Guid fileId, FileRecordDownloadRequest request,
        FileAttachmentHandler handler, HttpContext context, CancellationToken cancellationToken) =>
        Results.Ok(ApiResults.Ok(await handler.DownloadAsync(fileId, request, cancellationToken), context));
}
