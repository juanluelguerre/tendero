using ElGuerre.Tendero.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace ElGuerre.Tendero.Persistence.Tests;

/// <summary>
/// The model is built here and nothing else is: no container, no connection, no
/// migration. <see cref="DbContext.Model"/> is lazy, so touching it is what runs
/// every <c>IEntityTypeConfiguration</c> in the assembly.
///
/// These exist because of a rename the compiler could not see. Aggregates keep
/// their collections in private fields, and EF is told about them BY NAME —
/// <c>ComplexCollection&lt;List&lt;ProductImage&gt;, ProductImage&gt;("images")</c>.
/// A style sweep took the leading underscore off seven fields; the seven strings
/// in the configurations kept it, and every one of them stopped pointing at
/// anything. The build stayed green, `dotnet format` stayed green, and the only
/// tests that failed were the ones that need Docker to run at all.
/// </summary>
public sealed class ModelBindingTests
{
    /// <summary>
    /// Names a real Postgres and connects to none: the provider is needed to
    /// build a relational model, not to reach a server.
    /// </summary>
    private static IModel Model =>
        new TenderoDbContext(new DbContextOptionsBuilder<TenderoDbContext>()
            .UseNpgsql("Host=model.only;Database=tendero")
            .Options).Model;

    [Fact]
    public void The_model_can_be_built()
    {
        // A complex collection whose member does not exist throws right here.
        // The assertion is deliberately weak — the failure is the exception.
        Assert.NotEmpty(Model.GetEntityTypes());
    }

    [Fact]
    public void Every_property_the_configuration_names_is_backed_by_a_real_member()
    {
        List<string> unbound = [];

        foreach (var entityType in Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetDeclaredProperties())
            {
                // A shadow property is legitimate when EF invented it: the
                // foreign keys of an owned collection have no member on the CLR
                // type by design, and neither does a discriminator.
                if (!property.IsShadowProperty()
                    || property.IsForeignKey()
                    || property.IsPrimaryKey())
                {
                    continue;
                }

                unbound.Add($"{entityType.DisplayName()}.{property.Name}");
            }
        }

        Assert.Empty(unbound);
    }

    /// <summary>
    /// A configuration written and never registered does not fail: EF maps the
    /// entity by convention instead, in the public schema, with enums as integers
    /// and every string unbounded — and a migration generated from that looks
    /// entirely reasonable. It happened to the agent registry (P11-2): the context
    /// applies configurations one by one, and the new one was not on the list. So
    /// every configuration in the assembly must be one the model applied, and the
    /// sign is the one thing a convention never produces: a schema.
    /// </summary>
    [Fact]
    public void Every_entity_configuration_in_the_assembly_is_applied()
    {
        var configured = typeof(TenderoDbContext).Assembly.GetTypes()
            .SelectMany(type => type.GetInterfaces())
            .Where(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>))
            .Select(contract => contract.GetGenericArguments()[0])
            .Distinct()
            .ToList();

        Assert.NotEmpty(configured);
        Assert.All(configured, entity =>
            Assert.False(string.IsNullOrEmpty(Model.FindEntityType(entity)?.GetSchema()),
                $"{entity.Name} has a configuration the context never applied — it is mapped by convention."));
    }
}
