using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// La migración inicial tiene que producir EXACTAMENTE el esquema que produce el
/// modelo. Es la única forma de saber que la línea base no nació torcida, y el
/// fallo que evita es silencioso: <c>EnsureCreated</c> no hace nada si el
/// esquema ya existe, así que una base de datos de desarrollo creada antes de la
/// migración seguiría funcionando mientras diverge, sin un solo error.
///
/// Sigue valiendo cuando haya diez migraciones: si aplicarlas todas deja de
/// coincidir con el modelo, alguien editó una a mano o generó una contra un
/// modelo distinto. Es más fuerte que <c>migrations has-pending-model-changes</c>,
/// que compara el modelo con el snapshot — dos ficheros que se generan juntos y
/// pueden estar de acuerdo en el mismo error.
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

        // Diferencia en ambos sentidos: sobra algo o falta algo, y el mensaje
        // dice cuál para no tener que abrir psql.
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
    /// Columnas e índices como conjunto de cadenas comparables. Se consulta el
    /// catálogo en lugar de invocar pg_dump: no hace falta el binario, y el
    /// resultado no depende de la versión de las herramientas cliente.
    /// __EFMigrationsHistory queda fuera a propósito — sólo existe en el lado
    /// migrado, y su ausencia en el otro no es una diferencia de esquema.
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
