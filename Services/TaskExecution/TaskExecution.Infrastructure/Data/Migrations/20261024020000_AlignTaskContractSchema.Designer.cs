using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TaskExecution.Infrastructure.Data.Migrations;

[DbContext(typeof(TaskExecutionDbContext))]
[Migration("20261024020000_AlignTaskContractSchema")]
partial class AlignTaskContractSchema
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder.HasAnnotation("ProductVersion", "10.0.4")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);
        NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);
        TaskExecutionDbContextModelSnapshot.BuildCurrentModel(modelBuilder);
#pragma warning restore 612, 618
    }
}
