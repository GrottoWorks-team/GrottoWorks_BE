using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Web;

/// <summary>Helpers to wrap handler results into the success envelope (BuildingBlocks README §3).</summary>
public static class ApiResults
{
    public static ApiResponse<T> Ok<T>(T data, HttpContext httpContext) =>
        ApiResponse<T>.Create(data, CorrelationIdMiddlewareExtensions.GetCorrelationId(httpContext));

    public static PagedResponse<T> Page<T>(
        IReadOnlyList<T> data,
        long totalItems,
        int page,
        int size,
        HttpContext httpContext) =>
        PagedResponse<T>.Create(
            data,
            new PageMeta(
                page,
                size,
                totalItems,
                (long)Math.Ceiling(totalItems / (double)size)),
            CorrelationIdMiddlewareExtensions.GetCorrelationId(httpContext));
}
