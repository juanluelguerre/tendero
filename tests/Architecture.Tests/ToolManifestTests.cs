using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace ElGuerre.Tendero.Architecture.Tests;

/// <summary>
/// The EF tool and the EF runtime are the same version.
///
/// `dotnet-ef` is a **client of the model it is pointed at**, not of the SDK. A
/// tool older than the runtime cannot read the model it is handed, and a tool
/// newer than the runtime writes a migration the application then refuses to
/// apply — and neither failure says "version" in its message. The repository
/// already has the first half on record: the machine's global `dotnet ef` was
/// 10.0.2 and was useless against an 11 runtime.
///
/// So `.config/dotnet-tools.json` pins the tool and `Directory.Packages.props`
/// pins the runtime, and this asserts they say the same string. It is the drift
/// nobody notices: bumping the package and forgetting the manifest leaves a
/// build that is green and a `dotnet-ef` that is quietly wrong.
///
/// **Why the pin sits behind the SDK is a separate fact, and it is not drift.**
/// `Npgsql.EntityFrameworkCore.PostgreSQL` takes an EXACT dependency —
/// `[11.0.0-preview.6.26359.118]`, square brackets, not a floor — so EF moves
/// when the provider moves, never when the SDK does.
///
/// There is deliberately **no test for that half**, because NuGet enforces it
/// harder than a test could: bumping EF alone fails RESTORE with NU1107,
/// quoting the provider's own `(= 11.0.0-preview.6.26359.118)`. A rule that
/// duplicates a stronger one is a rule that will one day disagree with it.
///
/// What NuGet cannot see is `.config/dotnet-tools.json`. It takes no part in
/// restore, so nothing but this notices when the tool is left behind.
/// </summary>
public sealed class ToolManifestTests
{
    [Fact]
    public void The_ef_tool_is_pinned_to_the_ef_runtime()
    {
        var root = RepositoryRoot();

        var manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, ".config", "dotnet-tools.json")));

        var tool = manifest.RootElement
            .GetProperty("tools")
            .GetProperty("dotnet-ef")
            .GetProperty("version")
            .GetString();

        var packages = File.ReadAllText(Path.Combine(root, "Directory.Packages.props"));

        var design = Regex.Match(
            packages,
            """<PackageVersion Include="Microsoft\.EntityFrameworkCore\.Design" Version="([^"]+)" />""");

        Assert.True(design.Success, "Directory.Packages.props no longer pins Microsoft.EntityFrameworkCore.Design.");

        Assert.True(tool == design.Groups[1].Value,
            $"""
             .config/dotnet-tools.json pins dotnet-ef {tool}
             Directory.Packages.props pins EF Core Design {design.Groups[1].Value}

             They have to be the same build. The tool reads the model the
             runtime produced; one version apart and the failure arrives as a
             message that never mentions a version.
             """);
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
