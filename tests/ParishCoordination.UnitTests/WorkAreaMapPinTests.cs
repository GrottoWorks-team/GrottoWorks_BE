using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using ParishCoordination.Api.Requests;
using ParishCoordination.Application.WorkAreas;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;
using ParishCoordination.Infrastructure.Data;
using ParishCoordination.Infrastructure.Data.Migrations;
using ParishCoordination.Infrastructure.SystemExport;

namespace ParishCoordination.UnitTests;

public sealed class WorkAreaMapPinTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("""{"name":"Area"}""", true)]
    [InlineData("""{"name":"Area","mapX":0,"mapY":1}""", true)]
    [InlineData("""{"name":"Area","mapX":null,"mapY":null}""", true)]
    [InlineData("""{"name":"Area","mapX":0.5}""", false)]
    [InlineData("""{"name":"Area","mapX":null,"mapY":0.5}""", false)]
    [InlineData("""{"name":"Area","mapX":0,"mapY":1.00000001}""", false)]
    public void Create_validates_the_coordinate_pair(string json, bool valid)
    {
        var request = JsonSerializer.Deserialize<CreateWorkAreaRequest>(json, Json)!;
        Validator.TryValidateObject(request, new ValidationContext(request), [], true)
            .Should().Be(valid);
    }

    [Theory]
    [InlineData("""{}""", false, false, false)]
    [InlineData("""{"name":"Area"}""", false, false, true)]
    [InlineData("""{"mapX":0,"mapY":1}""", true, true, true)]
    [InlineData("""{"mapX":null,"mapY":null}""", true, true, true)]
    [InlineData("""{"mapX":null}""", true, false, false)]
    [InlineData("""{"mapY":0.3}""", false, true, false)]
    [InlineData("""{"mapX":0.5,"mapY":null}""", true, true, false)]
    [InlineData("""{"mapX":-0.00000001,"mapY":0.3}""", true, true, false)]
    public void Patch_distinguishes_missing_coordinates_from_explicit_null(
        string json, bool hasX, bool hasY, bool valid)
    {
        var request = JsonSerializer.Deserialize<UpdateWorkAreaRequest>(json, Json)!;
        request.HasMapX.Should().Be(hasX);
        request.HasMapY.Should().Be(hasY);
        Validator.TryValidateObject(request, new ValidationContext(request), [], true)
            .Should().Be(valid);
    }

    [Fact]
    public void Domain_validates_before_rounding_and_dto_returns_the_normalized_values()
    {
        var area = WorkArea.Create(Guid.NewGuid(), null, null, null, null, "Area", null, null, null,
            WorkAreaStatus.Planned, DateTimeOffset.UtcNow, latitude: 10m, longitude: 106m);
        WorkAreaDto.From(area).MapX.Should().BeNull();
        WorkAreaDto.From(area).MapY.Should().BeNull();

        Action incomplete = () => area.Update(area.Name, null, null, null, null, DateTimeOffset.UtcNow,
            updateMapPin: true, mapX: 0.5m);
        incomplete.Should().Throw<ArgumentException>();
        Action outOfRange = () => area.Update(area.Name, null, null, null, null, DateTimeOffset.UtcNow,
            updateMapPin: true, mapX: -0.00000001m, mapY: 0.5m);
        outOfRange.Should().Throw<ArgumentOutOfRangeException>();
        area.Version.Should().Be(1);

        area.Update(area.Name, null, null, null, null, DateTimeOffset.UtcNow,
            updateMapPin: true, mapX: 0.12345675m, mapY: 1m);
        var dto = WorkAreaDto.From(area);
        dto.MapX.Should().Be(0.1234568m);
        dto.MapY.Should().Be(1m);
        dto.Latitude.Should().Be(10m);
        dto.Longitude.Should().Be(106m);
        JsonSerializer.SerializeToElement(dto, Json).GetProperty("mapX").GetDecimal().Should().Be(0.1234568m);
    }

    [Fact]
    public void Model_migration_and_system_export_include_map_pin_columns()
    {
        using var db = new ParishCoordinationDbContext(new DbContextOptionsBuilder<ParishCoordinationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only").Options);
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(WorkArea))!;
        string[] columns = ["map_x", "map_y"];
        foreach (var (propertyName, columnName) in new[] { (nameof(WorkArea.MapX), columns[0]), (nameof(WorkArea.MapY), columns[1]) })
        {
            var property = entity.FindProperty(propertyName)!;
            property.IsNullable.Should().BeTrue();
            property.GetColumnName().Should().Be(columnName);
            property.GetPrecision().Should().Be(8);
            property.GetScale().Should().Be(7);
        }
        entity.GetCheckConstraints().Should().Contain(check => check.Name == "work_area_map_pin_valid");

        db.Database.GetMigrations().Should().Contain("20261126040000_AddWorkAreaMapPin");
        var migration = new AddWorkAreaMapPin();
        migration.UpOperations.OfType<AddColumnOperation>().Select(column => column.Name).Should().Equal(columns);
        migration.DownOperations.OfType<DropColumnOperation>().Select(column => column.Name).Should().Equal(columns);
        SystemExportSource.Columns["work_area"].Should().Contain(columns);
    }
}
