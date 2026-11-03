using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ParishCoordination.Api.Requests;
using ParishCoordination.Application.WorkAreas;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;
using ParishCoordination.Infrastructure.Data;

namespace ParishCoordination.UnitTests;

public sealed class WorkAreaCoordinateTests
{
    [Theory]
    [InlineData("{}", false, false)]
    [InlineData("{\"latitude\":null}", true, false)]
    [InlineData("{\"longitude\":106.1234567}", false, true)]
    public void Patch_tracks_presence_even_when_value_is_null(string json, bool latitude, bool longitude)
    {
        var request = JsonSerializer.Deserialize<UpdateWorkAreaRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        request.HasLatitude.Should().Be(latitude);
        request.HasLongitude.Should().Be(longitude);
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), errors, true)
            .Should().Be(latitude || longitude);
    }

    [Fact]
    public void Update_can_clear_one_coordinate_and_preserve_the_other()
    {
        var area = Create(10.1234567m, 106.1234567m);
        area.Update(area.Name, area.Description, null, null, null, DateTimeOffset.UtcNow,
            updateLatitude: true, latitude: null);
        area.Latitude.Should().BeNull();
        area.Longitude.Should().Be(106.1234567m);
        area.Version.Should().Be(2);
        WorkAreaDto.From(area).Longitude.Should().Be(area.Longitude);
    }

    [Theory]
    [InlineData(-90, -180)]
    [InlineData(90, 180)]
    public void Domain_accepts_boundary_values(int latitude, int longitude)
    {
        var area = Create(latitude, longitude);
        WorkAreaDto.From(area).Latitude.Should().Be(latitude);
        WorkAreaDto.From(area).Longitude.Should().Be(longitude);
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(0, -181)]
    public void Domain_rejects_coordinates_outside_ranges(int latitude, int longitude)
    {
        var action = () => Create(latitude, longitude);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Model_uses_nullable_decimal_coordinates_and_range_constraints()
    {
        using var db = new ParishCoordinationDbContext(new DbContextOptionsBuilder<ParishCoordinationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only").Options);
        var entity = db.Model.FindEntityType(typeof(WorkArea))!;
        foreach (var name in new[] { nameof(WorkArea.Latitude), nameof(WorkArea.Longitude) })
        {
            var property = entity.FindProperty(name)!;
            property.IsNullable.Should().BeTrue();
            property.GetPrecision().Should().Be(10);
            property.GetScale().Should().Be(7);
        }
    }

    private static WorkArea Create(decimal latitude, decimal longitude) => WorkArea.Create(
        Guid.NewGuid(), null, null, null, null, "Area", null, null, null,
        WorkAreaStatus.Planned, DateTimeOffset.UtcNow, latitude, longitude);
}
