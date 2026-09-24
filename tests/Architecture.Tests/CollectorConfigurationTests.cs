using System.Text.RegularExpressions;
using Xunit;

namespace ElGuerre.Tendero.Architecture.Tests;

/// <summary>
/// The collector configuration mounted into the Grafana stack (ADR 0036) keeps the
/// three things that each cost a failed run to find, and none of which any other
/// test would notice going missing.
///
/// Faro's CORS headers: `allowed_headers` replaces the defaults, so without
/// `content-type` every browser request dies in its preflight, and Faro sends two
/// more headers the receiver's documentation does not mention (P9b-2). The
/// exporter back to the Aspire dashboard: gzip is the collector's default and the
/// dashboard reads plain protobuf, so without `compression: none` it rejects every
/// export; and it has to sit in every pipeline, or the full mode quietly takes a
/// signal away from the dashboard it promised to keep (P9b-3). And the image's
/// `http://*` origin, which would let any page a browser visits post here.
///
/// A text check over the file, like <see cref="SeedFileWiringTests"/>: the
/// alternative is a YAML parser for five assertions.
/// </summary>
public sealed class CollectorConfigurationTests
{
    private static readonly string Configuration = File.ReadAllText(
        Path.Combine(RepositoryRoot(), "src", "AppHost", "observability", "otelcol-config.yaml"))
        .Replace("\r\n", "\n", StringComparison.Ordinal);

    [Theory]
    [InlineData("content-type")]
    [InlineData("x-faro-session-id")]
    [InlineData("idempotency-key")]
    public void The_faro_receiver_allows_every_header_faro_sends(string header)
    {
        var faroReceiver = Section(Configuration, "  faro:");

        Assert.Matches(new Regex($@"^\s+- {Regex.Escape(header)}\s*$", RegexOptions.Multiline), faroReceiver);
    }

    [Fact]
    public void The_exporter_to_the_aspire_dashboard_sends_uncompressed_protobuf()
    {
        var exporter = Section(Configuration, "  otlp_http/aspire:");

        Assert.Matches(new Regex(@"^\s+compression: none\s*$", RegexOptions.Multiline), exporter);
    }

    [Theory]
    [InlineData("traces")]
    [InlineData("metrics")]
    [InlineData("logs")]
    public void Every_signal_still_reaches_the_aspire_dashboard(string pipeline)
    {
        var service = Configuration[Configuration.IndexOf("\nservice:", StringComparison.Ordinal)..];
        var exporters = Regex.Match(service, $@"^    {pipeline}:\n(?:      .*\n)*?      exporters: \[(?<list>[^\]]*)\]", RegexOptions.Multiline);

        Assert.True(exporters.Success, $"No '{pipeline}' pipeline with an exporters list.");
        Assert.Contains("otlp_http/aspire", exporters.Groups["list"].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void No_receiver_accepts_every_origin() =>
        Assert.DoesNotMatch(new Regex(@"^\s+- (http://)?\*\s*$", RegexOptions.Multiline), Configuration);

    /// <summary>The block that starts at <paramref name="header"/> and ends at the next line indented no deeper.</summary>
    private static string Section(string text, string header)
    {
        var normalised = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var start = normalised.IndexOf("\n" + header + "\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"No '{header.Trim()}' block in the collector configuration.");

        var indent = header.Length - header.TrimStart().Length;
        var lines = normalised[(start + 1)..].Split('\n');
        var body = lines.Skip(1).TakeWhile(line => line.Length == 0 || line.StartsWith(new string(' ', indent + 1), StringComparison.Ordinal) || line.TrimStart().StartsWith('#'));

        return string.Join('\n', body);
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
