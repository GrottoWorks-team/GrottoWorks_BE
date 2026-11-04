using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using NotificationAudit.Infrastructure.SystemJobs;

// Administrative verification only. Business APIs never connect to another service's database.
try
{
if (args.Length == 3 && args[0] == "restore-objects")
{
    await RecoveryObjects.RestoreAsync(args[1], args[2]);
    return;
}
if (args.Length == 1 && args[0] == "replay-reporting-errors")
{
    await ReportingErrorReplay.RunAsync();
    return;
}
if (args.Length == 1 && args[0] == "replay-lifecycle-errors")
{
    await ReportingErrorReplay.RunAsync("parish-lifecycle-task-snapshots");
    return;
}
if (args.Length == 4 && args[0] == "decrypt")
{
    var key = Convert.FromBase64String(Environment.GetEnvironmentVariable("GROTTOWORKS_BACKUP_KEY")
        ?? throw new InvalidOperationException("The decryption key is not configured."));
    try
    {
        await using var encrypted = File.OpenRead(args[1]);
        await using var plain = new FileStream(args[2], FileMode.CreateNew, FileAccess.Write);
        var keyId = await BackupEncryptionStream.DecryptAsync(encrypted, plain, key);
        if (keyId != args[3]) throw new InvalidDataException("Unexpected backup key identity.");
        Console.WriteLine("Backup authenticated and decrypted.");
    }
    finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(key); }
    return;
}
if (args.Length != 3 || args[0] is not ("model" or "migrate" or "script" or "diff" or "schema" or "rollback"))
    throw new ArgumentException("Usage: model|migrate|script <service> <database>; decrypt <input> <new-output> <expected-key-id>");

var connection = new NpgsqlConnectionStringBuilder
{
    Host = Environment.GetEnvironmentVariable("VERIFY_POSTGRES_HOST") ?? "localhost",
    Port = int.Parse(Environment.GetEnvironmentVariable("VERIFY_POSTGRES_PORT") ?? "15432"),
    Username = Environment.GetEnvironmentVariable("VERIFY_POSTGRES_USER") ?? "postgres",
    Password = Environment.GetEnvironmentVariable("VERIFY_POSTGRES_PASSWORD")
        ?? throw new InvalidOperationException("VERIFY_POSTGRES_PASSWORD is required."),
    Database = args[2]
}.ConnectionString;

await using DbContext db = args[1] switch
{
    "Identity" => new Identity.Infrastructure.Data.IdentityDbContext(Options<Identity.Infrastructure.Data.IdentityDbContext>()),
    "ParishCoordination" => new ParishCoordination.Infrastructure.Data.ParishCoordinationDbContext(Options<ParishCoordination.Infrastructure.Data.ParishCoordinationDbContext>()),
    "TaskExecution" => new TaskExecution.Infrastructure.Data.TaskExecutionDbContext(Options<TaskExecution.Infrastructure.Data.TaskExecutionDbContext>()),
    "Resource" => new Resource.Infrastructure.Data.ResourceDbContext(Options<Resource.Infrastructure.Data.ResourceDbContext>()),
    "Readiness" => new Readiness.Infrastructure.Data.ReadinessDbContext(Options<Readiness.Infrastructure.Data.ReadinessDbContext>()),
    "Reporting" => new Reporting.Infrastructure.Data.ReportingDbContext(Options<Reporting.Infrastructure.Data.ReportingDbContext>()),
    "NotificationAudit" => new NotificationAudit.Infrastructure.Data.NotificationAuditDbContext(Options<NotificationAudit.Infrastructure.Data.NotificationAuditDbContext>()),
    _ => throw new ArgumentException("Unknown service.")
};

if (args[0] == "diff")
{
    var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot!;
    var previous = db.GetService<IModelRuntimeInitializer>().Initialize(snapshot.Model, designTime: true);
    var current = db.GetService<IDesignTimeModel>().Model;
    var differences = db.GetService<IMigrationsModelDiffer>().GetDifferences(previous.GetRelationalModel(), current.GetRelationalModel());
    foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(differences, current))
        Console.WriteLine(command.CommandText);
    return;
}
// Never suppress this check: a green build alone does not prove that migration metadata is valid.
if (db.Database.HasPendingModelChanges())
    throw new InvalidOperationException($"{args[1]} has model changes missing from its migration snapshot.");
if (args[0] is "migrate" or "schema" or "rollback")
{
    var expectedCluster = Environment.GetEnvironmentVariable("VERIFY_POSTGRES_CLUSTER_ID")
        ?? throw new InvalidOperationException("Set VERIFY_POSTGRES_CLUSTER_ID from the target container before database operations.");
    await using var check = new NpgsqlConnection(connection);
    await check.OpenAsync();
    await using var command = new NpgsqlCommand("SELECT system_identifier::text FROM pg_control_system()", check);
    var actualCluster = (string)(await command.ExecuteScalarAsync())!;
    if (actualCluster != expectedCluster)
        throw new InvalidOperationException("Connected PostgreSQL cluster differs from the verified target. No migration was applied.");
}
if (args[0] == "rollback")
{
    if (db is not ParishCoordination.Infrastructure.Data.ParishCoordinationDbContext parish || args[2] != "verify_parish_db")
        throw new ArgumentException("Rollback verification is restricted to the isolated Parish database.");
    await OutboxRollback.VerifyAsync(parish);
    return;
}
if (args[0] == "schema")
{
    await using var database = new NpgsqlConnection(connection);
    await database.OpenAsync();
    var columns = new Dictionary<(string Table, string Column), (bool Nullable, string Type)>();
    await using (var command = new NpgsqlCommand("SELECT c.relname,a.attname,NOT a.attnotnull,format_type(a.atttypid,a.atttypmod) FROM pg_attribute a JOIN pg_class c ON c.oid=a.attrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind IN ('r','p') AND a.attnum<>0 AND NOT a.attisdropped", database))
    await using (var reader = await command.ExecuteReaderAsync())
        while (await reader.ReadAsync()) columns[(reader.GetString(0), reader.GetString(1))] = (reader.GetBoolean(2), reader.GetString(3));
    var indexes = new HashSet<string>();
    await using (var command = new NpgsqlCommand("SELECT indexname FROM pg_indexes WHERE schemaname='public'", database))
    await using (var reader = await command.ExecuteReaderAsync())
        while (await reader.ReadAsync()) indexes.Add(reader.GetString(0));
    var model = db.GetService<IDesignTimeModel>().Model.GetRelationalModel();
    var mismatches = new List<string>();
    foreach (var table in model.Tables)
    {
        foreach (var column in table.Columns)
        {
            if (!columns.TryGetValue((table.Name, column.Name), out var stored))
                mismatches.Add($"Missing column {table.Name}.{column.Name}");
            else
            {
                if (stored.Nullable != column.IsNullable)
                    mismatches.Add($"Nullability mismatch {table.Name}.{column.Name}");
                if (!string.Equals(stored.Type, column.StoreType, StringComparison.OrdinalIgnoreCase))
                    mismatches.Add($"Type mismatch {table.Name}.{column.Name}: stored {stored.Type}, expected {column.StoreType}");
            }
        }
        foreach (var index in table.Indexes)
            if (!indexes.Contains(index.Name)) mismatches.Add($"Missing index {index.Name}");
    }
    if (mismatches.Count != 0) throw new InvalidOperationException(string.Join("; ", mismatches));
    Console.WriteLine($"{args[1]}: {model.Tables.Count()} tables match expected columns, types, nullability and indexes.");
    return;
}
if (args[0] == "migrate")
{
    var before = (await db.Database.GetPendingMigrationsAsync()).ToArray();
    await db.Database.MigrateAsync();
    var remaining = (await db.Database.GetPendingMigrationsAsync()).ToArray();
    if (remaining.Length != 0) throw new InvalidOperationException("Migrations remain unapplied.");
    Console.WriteLine($"{args[1]}: applied {before.Length} migrations to {args[2]}; pending=0.");
}
else if (args[0] == "script")
{
    var migrator = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(db);
    Console.WriteLine(migrator.GenerateScript());
}
else Console.WriteLine($"{args[1]}: migration snapshot matches current model.");

DbContextOptions<T> Options<T>() where T : DbContext =>
    new DbContextOptionsBuilder<T>().UseNpgsql(connection).Options;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"{exception.GetType().Name}: {exception.Message}");
    Environment.ExitCode = 1;
}
