using System.Net.Sockets;
using ElGuerre.Tendero.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// A real Postgres, one per run, shared by the whole collection.
///
/// These are the repository's first tests that touch a database. What they prove
/// means nothing against an in-memory provider: the jsonb converters, the complex
/// collections in JSON columns, the unique index on <c>(source, external_id)</c>
/// that IS the import's idempotency key, and the outbox drain inside the same
/// transaction.
///
/// With no Docker the tests skip with a reason rather than failing: a fresh clone
/// without Docker has to be able to run <c>dotnet test</c> and see green for what
/// it can check.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>The skip reason, or null when the container is running.</summary>
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
            // Docker answers but cannot give us a container (no image and no
            // network, quota, permissions). It is still a skip, not a failure of
            // the code under test.
            Unavailable = $"Could not start the Postgres container: {exception.Message}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    /// <summary>Call at the start of every test; skips when there is no container.</summary>
    public void SkipIfUnavailable()
    {
        if (Unavailable is not null)
            Assert.Skip(Unavailable);
    }

    /// <summary>
    /// A fresh database per test. Cheaper than it sounds — a CREATE DATABASE on a
    /// warm container — and it stops the order of the tests from mattering.
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
    /// Testcontainers needs a daemon; asking the socket is faster and more honest
    /// than catching an exception thirty seconds later.
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
