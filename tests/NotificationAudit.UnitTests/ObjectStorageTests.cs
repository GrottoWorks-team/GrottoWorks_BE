using System.Globalization;
using FluentAssertions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using NotificationAudit.Infrastructure.Files;
using Xunit;

namespace NotificationAudit.UnitTests;

public sealed class ObjectStorageTests
{
    [Fact]
    public void R2_endpoint_and_region_are_derived_from_the_account_id()
    {
        var options = new ObjectStorageOptions
        {
            Provider = ObjectStorageProvider.R2, AccountId = "abc123",
            // A leftover MinIO value must never end up in browser URLs.
            PublicEndpoint = "http://localhost:9000"
        };

        options.ResolveInternalEndpoint().Should().Be("https://abc123.r2.cloudflarestorage.com");
        options.ResolvePublicEndpoint().Should().Be("https://abc123.r2.cloudflarestorage.com");
        options.ResolveRegion().Should().Be("auto");
        options.ShouldManageBucket.Should().BeFalse();
        ObjectStorageClientFactory.ShouldDisablePayloadSigning(options).Should().BeTrue();
    }

    [Fact]
    public void R2_without_endpoint_or_account_is_rejected()
    {
        var options = new ObjectStorageOptions { Provider = ObjectStorageProvider.R2 };

        options.Invoking(value => value.ResolveInternalEndpoint()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MinIO_defaults_keep_the_local_docker_setup()
    {
        var options = new ObjectStorageOptions { PublicEndpoint = "http://localhost:9000/" };

        options.ResolveInternalEndpoint().Should().Be("http://minio:9000");
        options.ResolvePublicEndpoint().Should().Be("http://localhost:9000");
        options.ResolveRegion().Should().Be("us-east-1");
        options.ShouldManageBucket.Should().BeTrue();
        ObjectStorageClientFactory.ShouldDisablePayloadSigning(options).Should().BeFalse();
    }

    [Theory]
    [InlineData(5, 60)]
    [InlineData(600, 600)]
    [InlineData(691200, 604800)]
    public async Task R2_upload_url_is_path_style_https_and_signs_the_content_type(
        int requestedSeconds, int expectedSeconds)
    {
        using var clients = Clients(new ObjectStorageOptions
        {
            Provider = ObjectStorageProvider.R2, AccountId = "abc123", AccessKey = "key", SecretKey = "secret",
            Bucket = "grottoworks-files"
        });
        var storage = new S3FileObjectStorage(clients, TimeProvider.System);

        var startedAt = DateTimeOffset.UtcNow;
        var request = await storage.CreateUploadRequestAsync(
            "pending/u/f", "image/webp", TimeSpan.FromSeconds(requestedSeconds), default);
        var finishedAt = DateTimeOffset.UtcNow;

        var uri = new Uri(request.Url);
        uri.Scheme.Should().Be("https");
        uri.Host.Should().Be("abc123.r2.cloudflarestorage.com");
        uri.AbsolutePath.Should().Be("/grottoworks-files/pending/u/f");
        var signedAt = AssertValidLifetime(uri, request.ExpiresAt, startedAt, finishedAt, expectedSeconds);
        var query = QueryHelpers.ParseQuery(uri.Query);
        var signingDate = signedAt.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        query["X-Amz-Credential"].ToString().Should().Be($"key/{signingDate}/auto/s3/aws4_request");
        query["X-Amz-SignedHeaders"].ToString().Split(';').Should().Contain("content-type");
        request.Headers.Should().Contain("Content-Type", "image/webp");
    }

    [Theory]
    [InlineData(5, 60)]
    [InlineData(600, 600)]
    [InlineData(691200, 604800)]
    public async Task MinIO_download_url_uses_the_public_http_endpoint_and_clamps_the_lifetime(
        int requestedSeconds, int expectedSeconds)
    {
        using var clients = Clients(new ObjectStorageOptions
        {
            InternalEndpoint = "http://minio:9000", PublicEndpoint = "http://localhost:9000",
            AccessKey = "key", SecretKey = "secret"
        });
        var storage = new S3FileObjectStorage(clients, TimeProvider.System);

        var startedAt = DateTimeOffset.UtcNow;
        var request = await storage.CreateDownloadRequestAsync(
            "completed/u/f/a", TimeSpan.FromSeconds(requestedSeconds), default);
        var finishedAt = DateTimeOffset.UtcNow;

        var uri = new Uri(request.Url);
        uri.Scheme.Should().Be("http");
        uri.Authority.Should().Be("localhost:9000");
        uri.AbsolutePath.Should().Be("/grottoworks-files/completed/u/f/a");
        var signedAt = AssertValidLifetime(uri, request.ExpiresAt, startedAt, finishedAt, expectedSeconds);
        var query = QueryHelpers.ParseQuery(uri.Query);
        var signingDate = signedAt.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        query["X-Amz-Credential"].ToString().Should().Be($"key/{signingDate}/us-east-1/s3/aws4_request");
    }

    private static ObjectStorageClients Clients(ObjectStorageOptions options) => new(Options.Create(options));

    private static DateTimeOffset AssertValidLifetime(
        Uri uri, DateTimeOffset expiresAt, DateTimeOffset startedAt, DateTimeOffset finishedAt, int expectedSeconds)
    {
        var query = QueryHelpers.ParseQuery(uri.Query);
        var signedAt = DateTimeOffset.ParseExact(
            query["X-Amz-Date"].ToString(), "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        var expiresInSeconds = int.Parse(query["X-Amz-Expires"].ToString(), CultureInfo.InvariantCulture);

        signedAt.Should().BeOnOrAfter(startedAt.AddSeconds(-2));
        signedAt.Should().BeOnOrBefore(finishedAt.AddSeconds(2));
        expiresInSeconds.Should().BeInRange(1, expectedSeconds);
        var signedExpiry = signedAt.AddSeconds(expiresInSeconds);
        signedExpiry.Should().BeAfter(finishedAt);
        signedExpiry.Should().BeCloseTo(expiresAt, TimeSpan.FromSeconds(2));
        expiresAt.Should().BeOnOrAfter(startedAt.AddSeconds(expectedSeconds));
        expiresAt.Should().BeOnOrBefore(finishedAt.AddSeconds(expectedSeconds));

        return signedAt;
    }
}
