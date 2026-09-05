using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// Every command and query handler the API's assemblies contain can be
/// resolved from the API's own container.
///
/// Composition is the failure this repository keeps finding by hand: a worker
/// scanning only Search's assembly, Carter skipping an assembly that referenced
/// it transitively, a slice whose endpoint was live and whose handler was not
/// registered. <c>Program.cs</c> lists the assemblies <c>AddTenderoCqrs</c>
/// scans, and a seventh context added with an <c>AddX</c> call but not on that
/// list would compile, start and answer every request with "no handler for
/// this command" — from the first real user, not from a test.
///
/// The handlers are found by reflection over the assemblies the API references,
/// so a new one joins this check by existing.
/// </summary>
public sealed class HandlerCompositionTests
{
    private static readonly Type[] HandlerContracts = [typeof(ICommandHandler<,>), typeof(IQueryHandler<,>)];

    [Fact]
    public void Every_handler_in_a_referenced_assembly_is_registered()
    {
        using var factory = new TenderoApiFactory();
        _ = factory.CreateClient();

        var contracts = typeof(Program).Assembly.GetReferencedAssemblies()
            .Where(name => name.Name?.StartsWith("ElGuerre.Tendero.", StringComparison.Ordinal) == true)
            .Select(System.Reflection.Assembly.Load)
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
            .SelectMany(type => type.GetInterfaces())
            .Where(contract => contract.IsGenericType && HandlerContracts.Contains(contract.GetGenericTypeDefinition()))
            .Distinct()
            .ToArray();

        Assert.NotEmpty(contracts);

        using var scope = factory.Services.CreateScope();

        var unregistered = contracts
            .Where(contract => scope.ServiceProvider.GetService(contract) is null)
            .Select(contract => $"  {contract.GetGenericArguments()[0].Name}")
            .ToArray();

        Assert.True(
            unregistered.Length == 0,
            $"""
             These requests have a handler nobody registered — is its assembly on
             Program.cs's AddTenderoCqrs list?

             {string.Join(Environment.NewLine, unregistered)}
             """);
    }
}
