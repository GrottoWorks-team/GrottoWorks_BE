namespace Identity.Api.Common;

/// <summary>Small helpers to wrap handler results into the success envelope.</summary>
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
                (int)Math.Ceiling(totalItems / (double)size)),
            CorrelationIdMiddlewareExtensions.GetCorrelationId(httpContext));
}
