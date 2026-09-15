namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// The Compose labels that put a container under <c>tendero</c> in Docker Desktop,
/// the same two the AppHost applies to everything it starts.
///
/// Docker Desktop groups only on the Compose project label, so a container a test
/// starts without it is a loose row among the other projects on the machine —
/// indistinguishable from somebody's work Postgres while it runs. A label cannot
/// be added to a container after it exists, so it goes on the builder.
/// </summary>
internal static class DockerDesktopGroup
{
    public const string ProjectLabel = "com.docker.compose.project";
    public const string ServiceLabel = "com.docker.compose.service";

    public const string Project = "tendero";
}
