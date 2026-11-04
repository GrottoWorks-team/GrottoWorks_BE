using Xunit;
namespace Reporting.UnitTests;
public sealed class SystemExportAllowlistTests
{
    [Fact]
    public void Authentication_secrets_are_never_part_of_the_system_export_schema()
    {
        var columns=Reporting.Infrastructure.SystemExport.SystemExportSource.Columns;
        Assert.NotEmpty(columns);
        foreach(var column in columns.SelectMany(table=>table.Value))
        {
            Assert.False(column.Contains("password",StringComparison.OrdinalIgnoreCase));
            Assert.False(column.Contains("token",StringComparison.OrdinalIgnoreCase));
            Assert.False(column.Contains("secret",StringComparison.OrdinalIgnoreCase));
        }
    }
}
