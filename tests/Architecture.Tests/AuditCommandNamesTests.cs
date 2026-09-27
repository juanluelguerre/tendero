using System.Text.Json;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Architecture.Tests;

/// <summary>
/// Every command the shop audits has a name a shopkeeper reads, in both
/// languages (invariant 6; the redesign's R-1, B-F6).
///
/// The audit step records a command by its type — `typeof(TCommand).Name` — and
/// the audit screen showed exactly that: <c>CompleteCheckoutSessionCommand</c>,
/// the developer's language in the shopkeeper's screen. The screen now reads the
/// words from its translations, and this is what keeps them complete: a command
/// added tomorrow fails here, by name, until somebody says what it is called.
/// </summary>
public sealed class AuditCommandNamesTests
{
    private static readonly string[] Languages = ["en", "es"];

    [Fact]
    public void Every_command_has_a_name_in_every_language_of_the_backoffice()
    {
        var commands = Solution.All
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => !type.IsAbstract && type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>)))
            .Select(type => type.Name)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        // The guard every scan here carries: finding nothing would pass.
        Assert.True(commands.Count > 20, $"Only {commands.Count} commands were found; the scan is not seeing the solution.");

        var missing = Languages
            .SelectMany(language => commands
                .Where(command => !Names(language).ContainsKey(command))
                .Select(command => $"{language}: audit.commands.{command}"))
            .ToList();

        Assert.True(missing.Count == 0, "These commands have no name in the backoffice:\n" + string.Join('\n', missing));
    }

    [Fact]
    public void Names_no_command_that_does_not_exist()
    {
        var commands = Solution.All
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>)))
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        var stale = Languages
            .SelectMany(language => Names(language).Keys.Where(name => !commands.Contains(name)).Select(name => $"{language}: {name}"))
            .ToList();

        Assert.True(stale.Count == 0, "These names belong to no command any more:\n" + string.Join('\n', stale));
    }

    private static Dictionary<string, string> Names(string language)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepositoryRoot(), "frontend", "apps", "backoffice", "public", "i18n", $"{language}.json")));

        return document.RootElement.GetProperty("audit").TryGetProperty("commands", out var names)
            ? names.EnumerateObject().ToDictionary(entry => entry.Name, entry => entry.Value.GetString() ?? string.Empty, StringComparer.Ordinal)
            : [];
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
