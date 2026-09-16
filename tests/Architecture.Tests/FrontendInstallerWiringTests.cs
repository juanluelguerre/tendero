using System.Text.RegularExpressions;
using Xunit;

namespace ElGuerre.Tendero.Architecture.Tests;

/// <summary>
/// No Vite app in the AppHost gets an installer of its own.
///
/// In Aspire.Hosting.JavaScript 13.5.3 <c>AddViteApp</c> adds an npm installer
/// resource by default. Both Angular apps live in ONE Nx workspace over ONE
/// <c>node_modules</c>, so two installers ran npm in the same folder at the same
/// time: one finished, its dev server started, the other was still rewriting
/// <c>node_modules</c>, and a fresh clone of the public repository failed with
/// <c>'nx' is not recognized as an internal or external command</c>. The AppHost's
/// comment said "NOT <c>.WithNpm()</c>", which is not the same as no installer.
///
/// A text check for the reason <see cref="SeedFileWiringTests"/> gives: the
/// alternative is Aspire.Hosting.Testing, a dependency for one assertion.
/// </summary>
public sealed class FrontendInstallerWiringTests
{
    [Fact]
    public void Every_vite_app_opts_out_of_its_own_installer()
    {
        var appHost = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "AppHost", "AppHost.cs"));

        // Each AddViteApp call up to the end of its statement.
        var apps = Regex.Matches(appHost, @"builder\.AddViteApp\(""(?<name>[^""]+)""[^;]*;")
            .Select(match => (Name: match.Groups["name"].Value, Statement: match.Value))
            .ToArray();

        Assert.NotEmpty(apps);

        var installing = apps
            .Where(app => !app.Statement.Contains(".WithNpm(install: false)", StringComparison.Ordinal))
            .Select(app => app.Name)
            .ToArray();

        Assert.True(installing.Length == 0,
            $"""
             src/AppHost/AppHost.cs starts an npm installer for: {string.Join(", ", installing)}.

             AddViteApp installs by default, and the apps share one node_modules, so
             two installers race and a dev server starts while `nx` is missing.
             Add `.WithNpm(install: false)`; installing is the README's prerequisite.
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
