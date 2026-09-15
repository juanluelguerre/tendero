using System.Reflection;
using System.Text.RegularExpressions;
using ElGuerre.Tendero.Catalog.Connectors;
using ElGuerre.Tendero.SharedKernel;
using NetArchTest.Rules;
using Xunit;
using TestResult = NetArchTest.Rules.TestResult;

namespace ElGuerre.Tendero.Architecture.Tests;

/// <summary>
/// The rules from docs/testing.md, one per test. Each fails with the list of
/// guilty types: a message saying "something is wrong" is no use.
/// </summary>
public sealed class ArchitectureRules
{
    private const string DomainSuffix = ".Domain";

    /// <summary>
    /// The floor under rule 1. A context whose .csproj is still clean (today,
    /// Ordering) must not give a vacuous pass: these are the infrastructures the
    /// domain may not see, not even the day somebody adds the package.
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
            // EVERYTHING the assembly drags in for its slices is forbidden, plus
            // the assembly's own non-Domain namespaces. The list is computed, not
            // written: adding a package to the context puts it in the rule by itself.
            var forbidden = ForbiddenForDomainIn(assembly);
            Assert.NotEmpty(forbidden);

            // The domain is the heart: if it needs anything beyond the
            // SharedKernel, either the concept is misplaced or a port is missing.
            var result = Types.InAssembly(assembly)
                .That().ResideInNamespaceEndingWith(DomainSuffix)
                .ShouldNot().HaveDependencyOnAny(forbidden)
                .GetResult();

            Assert.True(result.IsSuccessful, Describe(assembly, result));
        }
    }

    /// <summary>
    /// A slice never reaches sideways. Share downwards (SharedKernel) or outwards
    /// (a port), never across.
    ///
    /// The selector is a regex and not <c>ResideInNamespace</c>, and that is not
    /// fussiness: <c>ResideInNamespace</c> matches by PREFIX, so the slice
    /// <c>GetProduct</c> also swept up every type in <c>GetProductImage</c>. The
    /// rule then reported the image endpoint depending on itself, and would
    /// equally have missed a genuine crossing between those two, because from
    /// inside the wrong grouping it does not look like a crossing at all.
    ///
    /// It went unnoticed for six phases because no slice had been named a prefix
    /// of another until the product page arrived — and then it went red without a
    /// crossing existing at all, which is the worse half: the rule could not tell
    /// its own false positive from the thing it is for. Verified by mutation:
    /// adding a reference from <c>GetProduct</c> to a type in
    /// <c>GetProductImage</c> turns this red, and removing it turns it green,
    /// which is what the prefix form could no longer do.
    /// </summary>
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

                var result = Types.InAssembly(assembly)
                    .That().ResideInNamespaceMatching(Exactly(slice))
                    .ShouldNot().HaveDependencyOnAny(others)
                    .GetResult();

                Assert.True(result.IsSuccessful, $"{slice}: {Describe(assembly, result)}");
            }
        }
    }

    /// <summary>
    /// The slice's own namespace and anything nested under it — and nothing that
    /// merely starts with the same letters.
    /// </summary>
    private static string Exactly(string @namespace) =>
        $@"^{Regex.Escape(@namespace)}($|\.)";

    [Fact]
    public void Connector_adapters_are_internal_so_only_the_port_is_public()
    {
        // A handler that can name SeedCatalogConnector will end up doing it.
        var result = Types.InAssembly(Solution.Catalog)
            .That().ImplementInterface(typeof(ICatalogSourceConnector))
            .And().AreClasses()
            .Should().NotBePublic()
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(Solution.Catalog, result));
    }

    /// <summary>
    /// The same rule 3, applied to the other port with an adapter. They were
    /// public without needing to be: they register from their own assembly, so
    /// nothing outside had any reason to be able to name them — and while it
    /// could, somebody would end up injecting ElasticsearchLexicalSearch instead
    /// of the port.
    /// </summary>
    [Theory]
    [InlineData("ElGuerre.Tendero.Search.Elasticsearch")]
    [InlineData("ElGuerre.Tendero.Search.Embeddings")]
    [InlineData("ElGuerre.Tendero.Search.Qdrant")]
    [InlineData("ElGuerre.Tendero.Search.Reranking")]
    public void Search_adapters_are_internal_so_only_the_port_is_public(string adapterNamespace)
    {
        var result = Types.InAssembly(Solution.Search)
            .That().ResideInNamespace(adapterNamespace)
            .And().AreClasses()
            .And().DoNotHaveNameEndingWith("Extensions")
            .Should().NotBePublic()
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(Solution.Search, result));
    }

    /// <summary>
    /// The model server is a detail behind <c>IEmbeddingGenerator</c>, exactly
    /// as Elasticsearch is behind the search ports. The OpenAI SDK is how TEI
    /// happens to be reached today (P8-2), and the day it is reached some other
    /// way — a second adapter, a hand-written client — only a folder changes.
    /// </summary>
    [Fact]
    public void The_openai_client_stays_inside_the_embeddings_adapter()
    {
        foreach (var assembly in Solution.All)
        {
            var result = Types.InAssembly(assembly)
                .That().DoNotResideInNamespace("ElGuerre.Tendero.Search.Embeddings")
                .ShouldNot().HaveDependencyOn("OpenAI")
                .GetResult();

            Assert.True(result.IsSuccessful, Describe(assembly, result));
        }
    }

    /// <summary>
    /// The vector store is a detail behind <c>IProductVectorIndexer</c> and
    /// <c>IVectorProductSearch</c>, the way Elasticsearch is behind the lexical
    /// ports (P8-4). A strategy that could name a <c>QdrantClient</c> would end up
    /// asking it for something the port does not offer.
    /// </summary>
    [Fact]
    public void The_qdrant_client_stays_inside_its_adapter()
    {
        foreach (var assembly in Solution.All)
        {
            var result = Types.InAssembly(assembly)
                .That().DoNotResideInNamespace("ElGuerre.Tendero.Search.Qdrant")
                .ShouldNot().HaveDependencyOn("Qdrant.Client")
                .GetResult();

            Assert.True(result.IsSuccessful, Describe(assembly, result));
        }
    }

    [Fact]
    public void The_elasticsearch_client_stays_inside_its_adapter()
    {
        foreach (var assembly in Solution.All)
        {
            // Elasticsearch is a detail behind IProductIndexer /
            // ILexicalProductSearch. The day it is replaced, only a folder changes.
            var result = Types.InAssembly(assembly)
                .That().DoNotResideInNamespace("ElGuerre.Tendero.Search.Elasticsearch")
                .ShouldNot().HaveDependencyOn("Elastic.Clients")
                .GetResult();

            Assert.True(result.IsSuccessful, Describe(assembly, result));
        }
    }

    /// <summary>
    /// A command result that carries an <c>Outcome</c> can be a refusal, and the
    /// audit log can only say so if the result tells it. Until 2026-09-11 none
    /// did, and a declined card, a refused return and an illegal transition were
    /// all recorded as Allowed — the audit screen opened on "the refusals" and
    /// showed none. This keeps the next slice from being the one that forgets.
    /// </summary>
    [Fact]
    public void A_command_result_that_can_be_a_refusal_tells_the_audit_log()
    {
        var results = Solution.Contexts
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetInterfaces())
            .Where(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(ICommand<>))
            .Select(contract => contract.GetGenericArguments()[0])
            .Distinct()
            .Where(result => result.GetProperty("Outcome") is not null)
            .ToArray();

        // Not vacuous: checkout, returns and publishing all return outcomes.
        Assert.NotEmpty(results);

        var silent = results
            .Where(result => !typeof(IAuditedResult).IsAssignableFrom(result))
            .Select(result => $"  {result.FullName}")
            .ToArray();

        Assert.True(silent.Length == 0,
            $"""
             These command results carry an Outcome and do not say which ones are refusals:

             {string.Join(Environment.NewLine, silent)}

             Implement IAuditedResult with a switch over the outcome and no default
             arm, so a new outcome cannot ship without that decision.
             """);
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
    /// Pricing knows the shared kernel and nothing else — not Catalog, not
    /// Ordering, not Persistence.
    ///
    /// This is not tidiness. It is the property the whole context is shaped
    /// around: the promotion engine takes values and returns values, so its
    /// combination rules can be verified with CsCheck over thousands of carts
    /// without a database. The day Pricing can load a Product, quoting needs
    /// Postgres and those properties stop being writable.
    ///
    /// The check is reflection over the actual assembly references rather than
    /// a hand-kept list, for the same reason rule 1 computes its forbidden set:
    /// a list somebody has to remember to update is a rule that stops being one.    ///
    /// **What this catches, exactly.** `GetReferencedAssemblies` reads the
    /// compiled manifest, and the compiler leaves out a reference nothing uses —
    /// so adding the ProjectReference alone passes, and the first line of code
    /// that actually touches the other context fails. That is the right
    /// semantics (the rule is about coupling, not about a line in an XML file)
    /// and it is worth knowing rather than discovering: verified by adding both
    /// the reference and a use of it, and watching this go red.
    /// </summary>
    [Fact]
    public void Pricing_knows_only_the_shared_kernel()
    {
        var sharedKernel = Solution.SharedKernel.GetName().Name;

        var contexts = Solution.Pricing.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .OfType<string>()
            .Where(name => name.StartsWith("ElGuerre.Tendero.", StringComparison.Ordinal)
                           && name != sharedKernel)
            .ToArray();

        Assert.True(contexts.Length == 0,
            $"Pricing references {string.Join(", ", contexts)}. It may reference only {sharedKernel}.");
    }

    /// <summary>
    /// Inventory knows nothing about Ordering.
    ///
    /// This is the direction of the whole phase, and it is the one that is easy
    /// to get backwards. Stock exists without orders — goods arrive, shelves are
    /// counted, a warehouse operator adjusts a number — so a warehouse that had
    /// to know what an order is would be the general thing depending on the
    /// specific one, and Inventory would stop being reusable by anything else
    /// that moves goods.
    ///
    /// So the saga lives in Ordering and calls <c>IStockLedger</c> in SKUs,
    /// quantities and an <c>OrderId</c> from the SharedKernel (ADR 0024). The
    /// textbook alternative — Inventory subscribing to <c>OrderPlaced</c> — needs
    /// exactly the reference this forbids.
    ///
    /// Computed from the assembly's own references rather than a hand-kept list,
    /// like every other rule here.    ///
    /// **What this catches, exactly.** `GetReferencedAssemblies` reads the
    /// compiled manifest, and the compiler leaves out a reference nothing uses —
    /// so adding the ProjectReference alone passes, and the first line of code
    /// that actually touches the other context fails. That is the right
    /// semantics (the rule is about coupling, not about a line in an XML file)
    /// and it is worth knowing rather than discovering: verified by adding both
    /// the reference and a use of it, and watching this go red.
    /// </summary>
    [Fact]
    public void Inventory_knows_only_the_shared_kernel()
    {
        var sharedKernel = Solution.SharedKernel.GetName().Name;

        var contexts = Solution.Inventory.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .OfType<string>()
            .Where(name => name.StartsWith("ElGuerre.Tendero.", StringComparison.Ordinal)
                           && name != sharedKernel)
            .ToArray();

        Assert.True(contexts.Length == 0,
            $"Inventory references {string.Join(", ", contexts)}. It may reference only {sharedKernel} — "
            + "stock exists without orders, and the saga lives in Ordering for that reason (ADR 0024).");
    }

    /// <summary>
    /// And Ordering only ever sees Inventory's PORTS.
    ///
    /// The reference exists, deliberately, and this is what keeps it to an
    /// interface: the moment a handler names a <c>StockItem</c> or a
    /// <c>Reservation</c>, two contexts share an entity and ADR 0014's whole
    /// point is gone. The crossing has to stay values in, values out.
    /// </summary>
    [Fact]
    public void Ordering_sees_inventorys_ports_and_never_its_domain()
    {
        var result = Types.InAssembly(Solution.Ordering)
            .ShouldNot().HaveDependencyOn("ElGuerre.Tendero.Inventory.Domain")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(Solution.Ordering, result));
    }

    /// <summary>
    /// Accounts knows only the shared kernel, and this one is load-bearing in a
    /// way the others are not.
    ///
    /// Ordering WILL need Accounts — an order history is per customer — so a
    /// dependency in this direction would close a cycle rather than merely
    /// widen the graph. That is why signing in is two requests instead of one:
    /// `POST /api/accounts/me` establishes the customer and
    /// `POST /api/cart/claim` attaches the basket, each in the context that owns
    /// its half.
    ///
    /// Accounts answers "who is this". It never asks what they bought.
    /// </summary>
    [Fact]
    public void Accounts_knows_only_the_shared_kernel()
    {
        var sharedKernel = Solution.SharedKernel.GetName().Name;

        var contexts = Solution.Accounts.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .OfType<string>()
            .Where(name => name.StartsWith("ElGuerre.Tendero.", StringComparison.Ordinal)
                           && name != sharedKernel)
            .ToArray();

        Assert.True(contexts.Length == 0,
            $"Accounts references {string.Join(", ", contexts)}. It may reference only {sharedKernel} — "
            + "Ordering will need Accounts for order history, so a dependency this way closes a cycle.");
    }

    /// <summary>
    /// And so does Catalog, for the same reason and under the same limit.
    ///
    /// The product page renders a variant picker where a sold-out size is
    /// DISABLED rather than absent, and it renders it once — a second round trip
    /// would let a shopper click a size the next response takes away. So the
    /// slice asks <c>IAvailabilityReader</c> for a set of SKUs and gets back a
    /// set of quantities.
    ///
    /// That is the whole crossing, and this is what holds it there. A page that
    /// named a <c>StockItem</c> would be Catalog reading Inventory's model, and
    /// the next thing it would want is a warehouse.
    /// </summary>
    [Fact]
    public void Catalog_sees_inventorys_ports_and_never_its_domain()
    {
        var result = Types.InAssembly(Solution.Catalog)
            .ShouldNot().HaveDependencyOn("ElGuerre.Tendero.Inventory.Domain")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(Solution.Catalog, result));
    }

    /// <summary>
    /// And Ordering sees Pricing through exactly ONE file.
    ///
    /// The reference is deliberate: checkout has to revalidate a quote against
    /// the same engine that issued it, or the fingerprint check is two systems
    /// agreeing about nothing (ADR 0016). What must not happen is the dependency
    /// spreading — a handler that reached for `IPriceResolver` itself would be a
    /// second pricing engine, and the day the two drifted the check would still
    /// pass.
    ///
    /// So the rule is the one the Elasticsearch client already lives under: the
    /// adapter may know, and nothing else may. Adding a second file that touches
    /// Pricing fails the build, which is the moment to ask whether the crossing
    /// still belongs in one place.
    /// </summary>
    [Fact]
    public void Ordering_sees_pricing_through_one_adapter_and_nowhere_else()
    {
        var result = Types.InAssembly(Solution.Ordering)
            .That().DoNotHaveName("PricingCartPricer")
            .ShouldNot().HaveDependencyOn("ElGuerre.Tendero.Pricing")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(Solution.Ordering, result));
    }

    /// <summary>
    /// What <paramref name="assembly"/>'s domain may not touch: the assemblies
    /// its .csproj references (minus the framework and the SharedKernel) and its
    /// own namespaces outside Domain.
    /// </summary>
    private static string[] ForbiddenForDomainIn(Assembly assembly)
    {
        var domainNamespace = assembly.GetName().Name + DomainSuffix;

        var externals = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .OfType<string>()
            .Where(name => !IsFramework(name) && name != typeof(LocalizedText).Assembly.GetName().Name);

        // Neither Domain itself nor its parent namespaces (Tendero.Catalog is a
        // prefix of Tendero.Catalog.Domain: forbidding it would forbid the domain
        // from itself).
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
    /// A slice is the folder under Features: Tendero.Catalog.Features.ImportProducts.
    /// They are discovered by reflection so a new slice joins the rule by itself.
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

    /// <summary>
    /// A transport implements no handler. It is the rule the MCP tools were
    /// written against, and until now nothing enforced it.
    ///
    /// Why it matters more than it looks: the tools are six lines each precisely
    /// because the answer already exists somewhere else. The moment one of them
    /// grows a handler, the shop has two implementations of the same question —
    /// one reached over HTTP and one over JSON-RPC — and only one of them has the
    /// validator, the audit decorator and the tests. That is not a style
    /// preference; it is how a second surface quietly becomes a second system.
    ///
    /// Verified by mutation: an IQueryHandler declared inside src/Mcp turns this
    /// red and names the type.
    /// </summary>
    [Fact]
    public void A_transport_implements_no_handler_of_its_own()
    {
        Type[] handlerContracts =
        [
            typeof(IQueryHandler<,>), typeof(ICommandHandler<,>), typeof(IDomainEventHandler<>)
        ];

        foreach (var transport in Solution.Transports)
        {
            var handlers = transport.GetTypes()
                .Where(type => type.GetInterfaces().Any(contract =>
                    contract.IsGenericType
                    && handlerContracts.Contains(contract.GetGenericTypeDefinition())))
                .Select(type => type.FullName)
                .OfType<string>()
                .Order()
                .ToArray();

            Assert.True(handlers.Length == 0,
                $"""
                 {transport.GetName().Name} implements handlers:

                 {string.Join(Environment.NewLine, handlers.Select(name => "  " + name))}

                 A transport carries an existing slice over a second protocol. If the
                 question has no handler yet, it needs a slice in the context that owns
                 it — not one here, where the validator and the audit decorator are not.
                 """);
        }
    }

    /// <summary>
    /// And a transport never reaches a domain.
    ///
    /// What it may name is a query record and a result record; what it may not
    /// name is an aggregate. The distinction is what keeps the wire flat — a
    /// tool that can see a <c>Product</c> is one refactor away from serialising
    /// a <c>ProductId</c> as <c>{"value":"…"}</c>, which CLAUDE.md warns about
    /// precisely because no unit test catches it.
    ///
    /// The forbidden list is computed from the contexts rather than written
    /// here, like every other rule in this file: a seventh context is inside the
    /// rule the day it exists, and the non-empty assertion is what stops the
    /// whole thing passing vacuously if the discovery ever breaks.
    /// </summary>
    [Fact]
    public void A_transport_never_reaches_a_domain()
    {
        var domains = Solution.Contexts
            .SelectMany(assembly => assembly.GetTypes())
            .Select(type => type.Namespace)
            .OfType<string>()
            .Where(@namespace => @namespace.EndsWith(DomainSuffix, StringComparison.Ordinal)
                                 || @namespace.Contains(DomainSuffix + ".", StringComparison.Ordinal))
            .Distinct()
            .Order()
            .ToArray();

        Assert.NotEmpty(domains);

        foreach (var transport in Solution.Transports)
        {
            var result = Types.InAssembly(transport)
                .ShouldNot().HaveDependencyOnAny(domains)
                .GetResult();

            Assert.True(result.IsSuccessful, Describe(transport, result));
        }
    }

    private static string Describe(Assembly assembly, TestResult result) =>
        result.IsSuccessful
            ? string.Empty
            : $"{assembly.GetName().Name}: {string.Join(", ", result.FailingTypeNames ?? [])}";
}
