using System.Text.RegularExpressions;
using Xunit;

namespace ElGuerre.Tendero.Architecture.Tests;

/// <summary>
/// Every port the AppHost pins is below Windows' dynamic range.
///
/// Windows hands out 49152–65535 to outgoing connections, and Hyper-V and WinNAT
/// reserve blocks of a hundred ports inside it that move on every restart. With
/// Postgres at 55432 and Grafana at 53000 the stack failed to start twice in two
/// days (2026-09-25 and 26): the reserved blocks happened to cover them, the
/// proxies could not bind, and the API waited forever for a database it was
/// never going to reach. Aspire's DCP says so on every start ("a port in the
/// ephemeral range"), and nothing but this test would stop the next constant
/// from going back there.
///
/// A text check, like <see cref="CollectorConfigurationTests"/>: the ports are
/// constants at the top of a top-level program, which reflection cannot reach.
/// </summary>
public sealed class AppHostPortTests
{
    private const int FirstDynamicPort = 49152;

    [Fact]
    public void Every_pinned_port_is_below_the_dynamic_range()
    {
        var appHost = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "AppHost", "AppHost.cs"));

        var ports = Regex.Matches(appHost, @"^const int (?<name>\w+Port) = (?<port>\d+);", RegexOptions.Multiline)
            .Select(match => (Name: match.Groups["name"].Value, Port: int.Parse(match.Groups["port"].Value)))
            .ToArray();

        Assert.True(ports.Length >= 10, $"Only {ports.Length} port constants found; the check would prove little.");

        var inside = ports.Where(port => port.Port >= FirstDynamicPort).Select(port => $"{port.Name} = {port.Port}").ToArray();
        Assert.True(inside.Length == 0,
            $"Ports inside Windows' dynamic range, which Hyper-V may reserve under them: {string.Join(", ", inside)}.");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tendero.slnx")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not find Tendero.slnx above the test's output directory.");
    }
}
