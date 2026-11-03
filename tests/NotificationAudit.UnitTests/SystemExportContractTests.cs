using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotificationAudit.Api.Endpoints;
using NotificationAudit.Application;
using NotificationAudit.Application.SystemJobs;
using NotificationAudit.BackupWorker;
using NotificationAudit.Infrastructure.Data;
using NotificationAudit.Infrastructure.SystemExport;
using Xunit;

namespace NotificationAudit.UnitTests;

public sealed class SystemExportContractTests
{
    [Fact]
    public void All_seven_sources_have_explicit_sql_allowlists_and_exclude_authentication_secrets()
    {
        var assembly = typeof(SystemExportContractTests).Assembly;
        foreach (var service in SystemServiceCatalog.All)
        {
            using var stream = assembly.GetManifestResourceStream($"SystemExportSources.{service}.cs");
            stream.Should().NotBeNull();
            using var reader = new StreamReader(stream!);
            var source = reader.ReadToEnd();
            source.Should().Contain("await using var writer = new StreamWriter",
                "HTTP export streams must not synchronously flush during disposal under Kestrel");
            source.Should().NotContain("SELECT *").And.NotContain("Serialize(entity");
            var tableMatches = Regex.Matches(source, "\\[\"(?<table>[a-z_]+)\"\\] = \\[(?<columns>[^;]+?)\\],");
            tableMatches.Should().NotBeEmpty();
            foreach (Match table in tableMatches)
            {
                table.Groups["table"].Value.Should().NotBe("refresh_token").And.NotBe("password_reset_token");
                var columns = Regex.Matches(table.Groups["columns"].Value, "\"([a-z_]+)\"").Select(match => match.Groups[1].Value).ToArray();
                columns.Should().NotBeEmpty();
                columns.Should().OnlyContain(column => !column.Contains("password") && !column.Contains("token") &&
                    !column.Contains("secret") && !column.Contains("credential") && !column.Contains("payload") && !column.Contains("download_url"));
            }
        }
    }

    [Fact]
    public void Notification_file_metadata_allowlist_contains_every_mapped_metadata_column()
    {
        using var db = new NotificationAuditDbContext(new DbContextOptionsBuilder<NotificationAuditDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only;Username=metadata_only").Options);
        var entity = db.Model.FindEntityType(typeof(NotificationAudit.Domain.Files.FileObject))!;
        var table = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table("file_object", null);
        var columns = entity.GetProperties().Select(property => property.GetColumnName(table)).Where(column => column != "xmin");
        SystemExportSource.Columns["file_object"].Should().BeEquivalentTo(columns);
    }

    [Fact]
    public void System_api_operation_ids_routes_and_admin_roles_match_reviewed_contract()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Services.AddRouting();
        builder.Services.AddApplication();
        builder.Services.AddScoped<SystemJobHandler>();
        using var app = builder.Build();
        app.MapSystemJobEndpoints();
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
        var expected = new Dictionary<string, string>
        {
            ["createSystemDataExport"] = "/api/v1/system-data-exports",
            ["startBackup"] = "/api/v1/backups",
            ["getBackupStatus"] = "/api/v1/backups/{jobId:guid}"
        };
        foreach (var (operation, path) in expected)
        {
            var endpoint = endpoints.Single(endpoint => endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == operation);
            endpoint.RoutePattern.RawText.Should().Be(path);
            endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>().SelectMany(policy => policy.Requirements)
                .OfType<RolesAuthorizationRequirement>().SelectMany(requirement => requirement.AllowedRoles).Should().Equal("ADMIN");
        }
    }

    [Fact]
    public void Pg_dump_uses_custom_snapshot_format_and_never_puts_password_in_arguments()
    {
        var options = new DatabaseBackupOptions();
        var start = PostgresSnapshotDump.CreateStartInfo(options,
            "Host=database-internal;Database=identity_db;Username=backup_reader;Password=private-value;SSL Mode=VerifyFull", "snapshot-id");
        start.UseShellExecute.Should().BeFalse();
        start.CreateNoWindow.Should().BeTrue();
        start.ArgumentList.Should().Contain("--format=custom").And.Contain("--snapshot=snapshot-id");
        string.Join(" ", start.ArgumentList).Should().NotContain("private-value").And.NotContain("backup_reader");
        start.Environment["PGPASSWORD"].Should().Be("private-value");
        start.Environment["PGSSLMODE"].Should().Be("verify-full");
    }
}
