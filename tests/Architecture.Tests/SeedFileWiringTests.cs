using Xunit;

namespace ElGuerre.Tendero.Architecture.Tests;

/// <summary>
/// Every committed seed file is wired into the AppHost.
///
/// This exists because of a failure that no test could see and the build could
/// not either. Each seed path defaults to something like
/// "seed/attributes.sample.json", relative to the CONTENT ROOT — src/Api/ for the
/// API, src/Workers/ for the worker — and neither has a seed folder. The AppHost
/// passed the absolute path for exactly one of the five.
///
/// Nothing failed. The readers are deliberately forgiving: a missing file means
/// no attribute definitions, no categories, no tariffs and no promotions, and
/// everything carries on with the previous behaviour. So the catalogue imported,
/// search answered, quotes came back — and phase 2's localized attributes,
/// phase 2's category branch and the whole of phase 3's pricing were silently
/// absent. Searching "cocina" found nothing and every quote answered at
/// catalogue price with no promotions, with a green build and 241 passing tests.
///
/// It is a text check, and that is a deliberate trade. The honest alternative is
/// Aspire.Hosting.Testing, a new dependency for one assertion; reading the file
/// costs nothing and catches the actual failure mode — a sixth seed file added
/// and nobody wired it.
/// </summary>
public sealed class SeedFileWiringTests
{
    [Fact]
    public void Every_seed_file_is_passed_to_the_projects_that_read_it()
    {
        var root = RepositoryRoot();
        var appHost = File.ReadAllText(Path.Combine(root, "src", "AppHost", "AppHost.cs"));

        var seeds = Directory.EnumerateFiles(Path.Combine(root, "seed"), "*.sample.json")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(seeds);

        var unwired = seeds.Where(file => !appHost.Contains(file, StringComparison.Ordinal)).ToArray();

        Assert.True(unwired.Length == 0,
            $"""
             src/AppHost/AppHost.cs does not name: {string.Join(", ", unwired)}.

             A seed file the AppHost does not pass an absolute path for resolves
             against each project's content root, is not found, and its reader
             degrades in silence. Add it to SeedFileEnvironment.Files.
             """);
    }

    /// <summary>
    /// Walks up looking for the solution file, the same way the OpenAPI contract
    /// test finds the committed document: a marker rather than a string of
    /// <c>..</c>, so the test survives the project moving or the bin's TFM
    /// changing.
    /// </summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tendero.slnx")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not find Tendero.slnx above the test's output directory.");
    }
}
