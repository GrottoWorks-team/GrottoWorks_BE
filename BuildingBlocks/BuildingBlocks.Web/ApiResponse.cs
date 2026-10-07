namespace BuildingBlocks.Web;

/// <summary>Success envelope per API Contract 4.2 / BuildingBlocks README §3: <c>{ data, meta: { correlationId } }</c>.</summary>
public sealed record ResponseMeta(string CorrelationId);

public sealed record ApiResponse<T>(T Data, ResponseMeta Meta)
{
    public static ApiResponse<T> Create(T data, string correlationId) =>
        new(data, new ResponseMeta(correlationId));
}

/// <summary>Collection envelope per API Contract 4.2: <c>{ data, page, meta }</c>.</summary>
public sealed record PageMeta(
    int Page,
    int Size,
    long TotalItems,
    long TotalPages);

public sealed record PagedResponse<T>(
    IReadOnlyList<T> Data,
    PageMeta Page,
    ResponseMeta Meta)
{
    public static PagedResponse<T> Create(
        IReadOnlyList<T> data,
        PageMeta page,
        string correlationId) =>
        new(data, page, new ResponseMeta(correlationId));
}
