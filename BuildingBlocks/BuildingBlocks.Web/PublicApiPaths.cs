using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Web;

public static class PublicApiPaths
{
    public static bool IsInternal(string? path) =>
        path is not null &&
        new PathString("/" + path.TrimStart('/')).StartsWithSegments("/internal");
}
