using BuildingBlocks.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NotificationAudit.Application.Files;

namespace NotificationAudit.Api.Controllers;

[ApiController]
[Route("api/v1/files")]
[Authorize]
public sealed class FilesController(ProxyUploadPendingHandler proxyUpload) : ControllerBase
{
    [HttpPut("{fileId:guid}/proxy-upload", Name = "proxyUploadFile")]
    [RequestSizeLimit(FileUploadLimits.DesignAssetBytes)]
    // 26/11/2026: thêm application/pdf để minh chứng sẵn sàng / hoá đơn PDF tải được qua máy chủ
    // khi bucket chưa cấu hình CORS (FE tự chuyển sang đường proxy).
    [Consumes(FileUploadLimits.BinaryContentType, FileUploadLimits.GlbContentType, "image/png", "image/jpeg",
        "image/webp", "application/pdf")]
    [Produces("application/json")]
    public async Task<IActionResult> ProxyUpload(
        Guid fileId,
        CancellationToken cancellationToken)
    {
        var contentType = Request.ContentType?.Split(';')[0].Trim().ToLowerInvariant()
            ?? "application/octet-stream";
        var result = await proxyUpload.HandleAsync(fileId, Request.Body, contentType, cancellationToken);
        return Ok(ApiResults.Ok(result, HttpContext));
    }
}
