using FluentAssertions;
using Microsoft.Extensions.Options;
using NotificationAudit.Infrastructure.Files;
using Xunit;

namespace NotificationAudit.UnitTests;

public sealed class ObjectStorageTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 8, 0, 0, TimeSpan.Zero);

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

    [Fact]
    public async Task R2_upload_url_is_path_style_https_and_signs_the_content_type()
    {
        using var clients = Clients(new ObjectStorageOptions
        {
            Provider = ObjectStorageProvider.R2, AccountId = "abc123", AccessKey = "key", SecretKey = "secret",
            Bucket = "grottoworks-files"
        });
        var storage = new S3FileObjectStorage(clients, new FixedClock(Now));

        var request = await storage.CreateUploadRequestAsync("pending/u/f", "image/webp", TimeSpan.FromMinutes(10), default);

        var uri = new Uri(request.Url);
        uri.Scheme.Should().Be("https");
        uri.Host.Should().Be("abc123.r2.cloudflarestorage.com");
        uri.AbsolutePath.Should().Be("/grottoworks-files/pending/u/f");
        uri.Query.Should().Contain("X-Amz-Credential=key%2F20261106%2Fauto%2Fs3%2Faws4_request");
        uri.Query.Should().Contain("content-type");
        request.Headers.Should().Contain("Content-Type", "image/webp");
        request.ExpiresAt.Should().Be(Now.AddMinutes(10));
    }

    [Fact]
    public async Task MinIO_download_url_uses_the_public_http_endpoint_and_clamps_the_lifetime()
    {
        using var clients = Clients(new ObjectStorageOptions
        {
            InternalEndpoint = "http://minio:9000", PublicEndpoint = "http://localhost:9000",
            AccessKey = "key", SecretKey = "secret"
        });
        var storage = new S3FileObjectStorage(clients, new FixedClock(Now));

        var request = await storage.CreateDownloadRequestAsync("completed/u/f/a", TimeSpan.FromSeconds(5), default);

        var uri = new Uri(request.Url);
        uri.Scheme.Should().Be("http");
        uri.Authority.Should().Be("localhost:9000");
        uri.AbsolutePath.Should().Be("/grottoworks-files/completed/u/f/a");
        uri.Query.Should().Contain("us-east-1");
        request.ExpiresAt.Should().Be(Now.AddMinutes(1));
    }

    private static ObjectStorageClients Clients(ObjectStorageOptions options) => new(Options.Create(options));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
