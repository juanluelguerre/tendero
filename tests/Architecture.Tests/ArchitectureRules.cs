using System.Reflection;
using NetArchTest.Rules;
using TestResult = NetArchTest.Rules.TestResult;
using Tendero.Catalog.Connectors;
using Tendero.SharedKernel;
using Xunit;

namespace Tendero.Architecture.Tests;

/// <summary>
/// Las cinco reglas de docs/testing.md, una por test. Cada una falla con la
/// lista de tipos culpables: un mensaje que dice "algo está mal" no sirve.
/// </summary>
public sealed class ArchitectureRules
{
    private const string DomainSuffix = ".Domain";

    /// <summary>
    /// Suelo de la regla 1. Un contexto cuyo .csproj todavía está limpio (hoy,
    /// Ordering) no debe dar un aprobado vacío: estas son las infraestructuras
    /// que el dominio no puede ver ni el día que alguien añada el paquete.
    /// </summary>
    private static readonly string[] InfrastructureNamespaces =
    [
        "Carter",
        "Elastic",
        "FluentValidation",
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Extensions",
        "Npgsql",
        "OpenTelemetry"
    ];

    [Fact]
    public void Domain_code_depends_on_the_shared_kernel_and_nothing_else()
    {
        foreach (var assembly in Solution.Contexts)
        {
            // Se prohíbe TODO lo que el ensamblado arrastra para sus slices más
            // los namespaces no-Domain del propio ensamblado. La lista se calcula,
            // no se escribe: añadir un paquete al contexto lo mete en la regla solo.
            var forbidden = ForbiddenForDomainIn(assembly);
            Assert.NotEmpty(forbidden);

            // El dominio es el corazón: si necesita algo más que el SharedKernel,
            // o el concepto está mal colocado o hace falta un puerto.
            var result = Types.InAssembly(assembly)
                .That().ResideInNamespaceEndingWith(DomainSuffix)
                .ShouldNot().HaveDependencyOnAny(forbidden)
                .GetResult();

            Assert.True(result.IsSuccessful, Describe(assembly, result));
        }
    }

    [Fact]
    public void Slices_never_reference_another_slice()
    {
        foreach (var assembly in Solution.Contexts)
        {
            var slices = SliceNamespacesOf(assembly);

            foreach (var slice in slices)
            {
                var others = slices.Where(other => other != slice).ToArray();
                if (others.Length == 0)
                    continue;

                // Compartir baja (SharedKernel) o sale (un puerto). Nunca de lado.
                var result = Types.InAssembly(assembly)
                    .That().ResideInNamespace(slice)
                    .ShouldNot().HaveDependencyOnAny(others)
                    .GetResult();

                Assert.True(result.IsSuccessful, $"{slice}: {Describe(assembly, result)}");
            }
        }
    }

    [Fact]
    public void Connector_adapters_are_internal_so_only_the_port_is_public()
    {
        // Un handler que pueda nombrar SeedCatalogConnector acabará haciéndolo.
        var result = Types.InAssembly(Solution.Catalog)
            .That().ImplementInterface(typeof(ICatalogSourceConnector))
            .And().AreClasses()
            .Should().NotBePublic()
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(Solution.Catalog, result));
    }

    /// <summary>
    /// La misma regla 3, aplicada al otro puerto con adaptador. Estaban public
    /// sin necesitarlo: se registran desde su propio ensamblado, así que nada
    /// fuera tenía por qué poder nombrarlos — y mientras se pudiera, alguien
    /// acabaría inyectando ElasticsearchLexicalSearch en vez del puerto.
    /// </summary>
    [Fact]
    public void Search_adapters_are_internal_so_only_the_port_is_public()
    {
        var result = Types.InAssembly(Solution.Search)
            .That().ResideInNamespace("Tendero.Search.Elasticsearch")
            .And().AreClasses()
            .And().DoNotHaveNameEndingWith("Extensions")
            .Should().NotBePublic()
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(Solution.Search, result));
    }

    [Fact]
    public void The_elasticsearch_client_stays_inside_its_adapter()
    {
        foreach (var assembly in Solution.All)
        {
            // Elasticsearch es un detalle detrás de IProductIndexer /
            // ILexicalProductSearch. El día que se sustituya, sólo cambia una carpeta.
            var result = Types.InAssembly(assembly)
                .That().DoNotResideInNamespace("Tendero.Search.Elasticsearch")
                .ShouldNot().HaveDependencyOn("Elastic.Clients")
                .GetResult();

            Assert.True(result.IsSuccessful, Describe(assembly, result));
        }
    }

    [Fact]
    public void Domain_types_never_reference_entity_framework()
    {
        foreach (var assembly in Solution.Contexts)
        {
            var result = Types.InAssembly(assembly)
                .That().ResideInNamespaceEndingWith(DomainSuffix)
                .ShouldNot().HaveDependencyOn("Microsoft.EntityFrameworkCore")
                .GetResult();

            Assert.True(result.IsSuccessful, Describe(assembly, result));
        }
    }

    /// <summary>
    /// Lo que el dominio de <paramref name="assembly"/> no puede tocar: los
    /// ensamblados que su .csproj referencia (menos el framework y el
    /// SharedKernel) y sus propios namespaces fuera de Domain.
    /// </summary>
    private static string[] ForbiddenForDomainIn(Assembly assembly)
    {
        var domainNamespace = assembly.GetName().Name + DomainSuffix;

        var externals = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .OfType<string>()
            .Where(name => !IsFramework(name) && name != typeof(LocalizedText).Assembly.GetName().Name);

        // Ni el propio Domain ni sus namespaces padre (Tendero.Catalog es prefijo
        // de Tendero.Catalog.Domain: prohibirlo prohibiría el dominio consigo mismo).
        var siblings = assembly.GetTypes()
            .Select(type => type.Namespace)
            .OfType<string>()
            .Where(@namespace => !IsSameOrAncestorOf(@namespace, domainNamespace)
                                 && !IsSameOrAncestorOf(domainNamespace, @namespace));

        return [.. externals.Concat(siblings).Concat(InfrastructureNamespaces).Distinct().Order()];
    }

    private static bool IsSameOrAncestorOf(string candidate, string @namespace) =>
        @namespace == candidate
        || @namespace.StartsWith(candidate + ".", StringComparison.Ordinal);

    private static bool IsFramework(string assemblyName) =>
        assemblyName.StartsWith("System", StringComparison.Ordinal)
        || assemblyName is "netstandard" or "mscorlib";

    /// <summary>
    /// Un slice es la carpeta bajo Features: Tendero.Catalog.Features.ImportProducts.
    /// Se descubren por reflexión para que un slice nuevo entre en la regla solo.
    /// </summary>
    private static string[] SliceNamespacesOf(Assembly assembly) =>
        [.. assembly.GetTypes()
            .Select(type => type.Namespace)
            .OfType<string>()
            .Select(SliceNamespaceOrNull)
            .OfType<string>()
            .Distinct()
            .Order()];

    private static string? SliceNamespaceOrNull(string @namespace)
    {
        const string marker = ".Features.";

        var start = @namespace.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            return null;

        var sliceStart = start + marker.Length;
        var end = @namespace.IndexOf('.', sliceStart);

        return end < 0 ? @namespace : @namespace[..end];
    }

    private static string Describe(Assembly assembly, TestResult result) =>
        result.IsSuccessful
            ? string.Empty
            : $"{assembly.GetName().Name}: {string.Join(", ", result.FailingTypeNames ?? [])}";
}
