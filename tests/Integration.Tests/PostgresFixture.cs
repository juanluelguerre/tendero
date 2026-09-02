using System.Net.Sockets;
using ElGuerre.Tendero.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// Un Postgres real, uno por ejecución, compartido por toda la colección.
///
/// Estos son los primeros tests del repositorio que tocan una base de datos. Lo
/// que prueban no significa nada contra un proveedor en memoria: los
/// convertidores jsonb, las colecciones complejas en columnas JSON, el índice
/// único sobre <c>(source, external_id)</c> que ES la clave de idempotencia de
/// la importación, y el volcado del outbox dentro de la misma transacción.
///
/// Si no hay Docker, los tests se saltan con un motivo en vez de fallar: un
/// clon recién hecho sin Docker debe poder correr <c>dotnet test</c> y ver verde
/// lo que sí puede comprobar.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>Motivo del salto, o null si el contenedor está en marcha.</summary>
    public string? Unavailable { get; private set; }

    public async ValueTask InitializeAsync()
    {
        if (!DockerIsListening())
        {
            Unavailable = "Docker is not available on this machine, so the integration tests cannot run.";
            return;
        }

        try
        {
            _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }
        catch (Exception exception)
        {
            // Docker responde pero no puede darnos un contenedor (sin imagen y
            // sin red, cuota, permisos). Sigue siendo un salto, no un fallo del
            // código bajo prueba.
            Unavailable = $"Could not start the Postgres container: {exception.Message}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    /// <summary>Llamar al principio de cada test; salta si no hay contenedor.</summary>
    public void SkipIfUnavailable()
    {
        if (Unavailable is not null)
            Assert.Skip(Unavailable);
    }

    /// <summary>
    /// Una base nueva por test. Es más barato que parece —CREATE DATABASE sobre
    /// un contenedor caliente— y evita que el orden de los tests importe.
    /// </summary>
    public async Task<TenderoDbContextFactory> CreateDatabaseAsync(string name)
    {
        await using var admin = new Npgsql.NpgsqlConnection(ConnectionString);
        await admin.OpenAsync();
        await using (var command = admin.CreateCommand())
        {
            command.CommandText = $"""DROP DATABASE IF EXISTS "{name}" WITH (FORCE); CREATE DATABASE "{name}";""";
            await command.ExecuteNonQueryAsync();
        }

        var builder = new Npgsql.NpgsqlConnectionStringBuilder(ConnectionString) { Database = name };
        return new TenderoDbContextFactory(builder.ConnectionString);
    }

    /// <summary>
    /// TestContainers necesita un daemon; preguntarle al socket es más rápido y
    /// más honesto que atrapar una excepción treinta segundos después.
    /// </summary>
    private static bool DockerIsListening()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var pipe = new System.IO.Pipes.NamedPipeClientStream(
                    ".", "docker_engine", System.IO.Pipes.PipeDirection.InOut);
                pipe.Connect(TimeSpan.FromSeconds(2));
                return true;
            }

            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint("/var/run/docker.sock"));
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public sealed class TenderoDbContextFactory(string connectionString)
{
    public string ConnectionString { get; } = connectionString;

    public TenderoDbContext Create() =>
        new(new DbContextOptionsBuilder<TenderoDbContext>().UseNpgsql(ConnectionString).Options);
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
