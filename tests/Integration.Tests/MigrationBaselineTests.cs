using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// The initial migration has to produce EXACTLY the schema the model produces.
/// It is the only way to know the baseline was not born crooked, and the failure
/// it prevents is silent: <c>EnsureCreated</c> does nothing when the schema
/// already exists, so a development database created before the migration would
/// keep working while it diverged, without a single error.
///
/// It still holds with ten migrations: if applying them all stops matching the
/// model, somebody hand-edited one or generated one against a different model.
/// It is stronger than <c>migrations has-pending-model-changes</c>, which
/// compares the model with the snapshot — two files generated together, which can
/// agree on the same mistake.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MigrationBaselineTests(PostgresFixture postgres)
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task The_migrations_produce_the_same_schema_as_the_model()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        var fromModel = await postgres.CreateDatabaseAsync("baseline_model");
        await using (var context = fromModel.Create())
            await context.Database.EnsureCreatedAsync(ct);

        var fromMigrations = await postgres.CreateDatabaseAsync("baseline_migrations");
        await using (var context = fromMigrations.Create())
            await context.Database.MigrateAsync(ct);

        var model = await DescribeSchemaAsync(fromModel.ConnectionString, ct);
        var migrated = await DescribeSchemaAsync(fromMigrations.ConnectionString, ct);

        // The difference both ways: something extra or something missing, and the
        // message says which, so nobody has to open psql.
        var missing = model.Except(migrated).ToArray();
        var extra = migrated.Except(model).ToArray();

        Assert.True(
            missing.Length == 0 && extra.Length == 0,
            $"""
             The initial migration does not reproduce the model's schema.

             Only in the model (the migration forgot these):
             {string.Join(Environment.NewLine, missing.DefaultIfEmpty("  (none)"))}

             Only in the migration (it invented these):
             {string.Join(Environment.NewLine, extra.DefaultIfEmpty("  (none)"))}
             """);
    }

    /// <summary>
    /// Columns and indexes as a comparable set of strings. The catalog is queried
    /// instead of shelling out to pg_dump: no binary is needed, and the result
    /// does not depend on the client tools' version. __EFMigrationsHistory is
    /// left out on purpose — it only exists on the migrated side, and its absence
    /// on the other is not a schema difference.
    /// </summary>
    private static async Task<HashSet<string>> DescribeSchemaAsync(string connectionString, CancellationToken ct)
    {
        const string sql = """
            SELECT 'column  ' || table_schema || '.' || table_name || '.' || column_name
                   || ' ' || data_type
                   || ' null=' || is_nullable
                   || ' default=' || COALESCE(column_default, '-')
                   || ' len=' || COALESCE(character_maximum_length::text, '-')
                   || ' prec=' || COALESCE(numeric_precision::text, '-')
                   || ' scale=' || COALESCE(numeric_scale::text, '-')
            FROM information_schema.columns
            WHERE table_schema IN ('catalog', 'ordering', 'outbox')
            UNION ALL
            SELECT 'index   ' || schemaname || '.' || indexname || ' :: ' || indexdef
            FROM pg_indexes
            WHERE schemaname IN ('catalog', 'ordering', 'outbox')
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var described = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            described.Add("  " + reader.GetString(0));

        Assert.NotEmpty(described);
        return described;
    }
}
