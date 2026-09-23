using System.Reflection;
using System.Text;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Architecture.Tests;

/// <summary>
/// Writes <c>docs/domain-model.md</c> from the domain assemblies: one class diagram
/// per context, its events, and every transition table drawn as a state machine.
///
/// It is derived and never drawn, for the reason the OpenAPI document is: a model
/// someone draws by hand is a second source of truth, and nothing notices the day
/// it stops being true. Everything here is read by reflection from the public
/// surface of each <c>*.Domain</c> namespace, so a renamed property or a new edge in
/// a transition table changes the document, and <see cref="DomainModelDocumentTests"/>
/// fails until it is regenerated.
///
/// It says only what the types say. A record is marked <c>record</c> and an
/// <see cref="AggregateRoot"/> is marked <c>aggregate</c>; nothing is labelled an
/// entity or a value object, because the code declares no such thing and the
/// document would be guessing.
/// </summary>
internal static class DomainModelDocument
{
    private static readonly (string Name, Assembly Assembly)[] Contexts =
    [
        ("Catalog", Solution.Catalog),
        ("Pricing", Solution.Pricing),
        ("Inventory", Solution.Inventory),
        ("Ordering", Solution.Ordering),
        ("Accounts", Solution.Accounts),
    ];

    private static readonly NullabilityInfoContext Nullability = new();

    public static string Render()
    {
        var document = new StringBuilder();
        document.Append("""
            # Domain model

            > **Generated — do not edit.** Written from the code by
            > `tests/Architecture.Tests/DomainModelDocument.cs`, and a test fails when this
            > file and the code disagree. Regenerate it with:
            >
            > ```bash
            > UPDATE_DOMAIN_MODEL=1 dotnet test --project tests/Architecture.Tests
            > ```

            One section per bounded context, from the public types of its `*.Domain`
            namespace. Types from the SharedKernel — `Money`, `LocalizedText`, `Address`
            and the strongly-typed ids — appear as property types rather than as boxes,
            because every context uses them and drawing them five times says nothing.
            Contexts share no entities (ADR 0014), so no arrow crosses a section.

            `aggregate` marks a subclass of `AggregateRoot`; `record` marks a C# record.
            Events are listed rather than drawn: an aggregate raises them from its
            methods, which a diagram of properties cannot show. Every event also carries
            `OccurredAt`. The state machines are the aggregates' `TransitionTable`s, read
            from the tables themselves (invariant 5).

            """);

        foreach (var (name, assembly) in Contexts)
        {
            RenderContext(document, name, assembly);
        }

        return document.ToString().ReplaceLineEndings("\n");
    }

    private static void RenderContext(StringBuilder document, string context, Assembly assembly)
    {
        var domain = $"ElGuerre.Tendero.{context}.Domain";
        var types = assembly.GetExportedTypes()
            .Where(t => t.Namespace == domain && !t.IsNested)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();

        var events = types.Where(t => typeof(IDomainEvent).IsAssignableFrom(t) && !t.IsInterface).ToArray();
        var drawn = types
            .Where(t => !events.Contains(t)
                && !(t.IsAbstract && t.IsSealed)            // static helpers carry no state
                && !typeof(Exception).IsAssignableFrom(t)
                && !t.IsInterface)
            .ToHashSet();

        document.Append($"\n## {context}\n\n```mermaid\nclassDiagram\n");

        foreach (var type in drawn.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            RenderClass(document, type);
        }

        foreach (var type in drawn.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            RenderRelations(document, type, drawn);
        }

        document.Append("```\n");

        if (events.Length > 0)
        {
            document.Append("\n### Events\n\n| Event | Carries |\n|---|---|\n");
            foreach (var @event in events)
            {
                var carries = Properties(@event)
                    .Where(p => p.Name != nameof(IDomainEvent.OccurredAt))
                    .Select(p => $"`{p.Name}: {TypeName(p)}`");
                document.Append($"| `{@event.Name}` | {string.Join(", ", carries)} |\n");
            }
        }

        foreach (var type in drawn.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            RenderStateMachine(document, type);
        }
    }

    private static void RenderClass(StringBuilder document, Type type)
    {
        document.Append($"    class {type.Name} {{\n");

        if (type.IsEnum)
        {
            document.Append("        <<enumeration>>\n");
            foreach (var value in Enum.GetNames(type))
            {
                document.Append($"        {value}\n");
            }
        }
        else
        {
            if (typeof(AggregateRoot).IsAssignableFrom(type))
                document.Append("        <<aggregate>>\n");
            else if (IsRecord(type))
                document.Append("        <<record>>\n");

            if (type.IsAbstract)
                document.Append("        <<abstract>>\n");

            foreach (var property in Properties(type))
            {
                document.Append($"        +{TypeName(property)} {property.Name}\n");
            }
        }

        document.Append("    }\n");
    }

    private static void RenderRelations(StringBuilder document, Type type, HashSet<Type> drawn)
    {
        if (type.BaseType is { } parent && drawn.Contains(parent))
            document.Append($"    {parent.Name} <|-- {type.Name}\n");

        if (type.IsEnum)
            return;

        foreach (var property in Properties(type))
        {
            var (target, many) = Unwrap(property.PropertyType);

            // An enum is already legible as the property's type; an arrow to it
            // doubles the lines of every diagram and adds nothing.
            if (!drawn.Contains(target) || target.IsEnum)
                continue;

            document.Append(many
                ? $"    {type.Name} \"1\" --> \"*\" {target.Name} : {property.Name}\n"
                : $"    {type.Name} --> {target.Name} : {property.Name}\n");
        }
    }

    private static void RenderStateMachine(StringBuilder document, Type type)
    {
        var field = type
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .SingleOrDefault(f => f.FieldType.IsGenericType
                && f.FieldType.GetGenericTypeDefinition() == typeof(TransitionTable<>));

        if (field?.GetValue(null) is not { } table)
            return;

        var status = field.FieldType.GetGenericArguments()[0];
        var indexer = field.FieldType.GetProperty("Item")!;

        document.Append($"\n### {type.Name} states\n\n```mermaid\nstateDiagram-v2\n");

        foreach (var from in Enum.GetValues(status))
        {
            IEnumerable<object> targets;
            try
            {
                targets = ((System.Collections.IEnumerable)indexer.GetValue(table, [from])!).Cast<object>();
            }
            catch (TargetInvocationException)
            {
                continue;                                   // no edges declared from this state
            }

            foreach (var to in targets)
            {
                document.Append($"    {from} --> {to}\n");
            }
        }

        document.Append("```\n");
    }

    private static IEnumerable<PropertyInfo> Properties(Type type) =>
        type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(p => p.GetIndexParameters().Length == 0)
            .OrderBy(p => p.MetadataToken);

    private static bool IsRecord(Type type) => type.GetMethod("<Clone>$") is not null
        || (type.IsValueType && type.GetMethod("PrintMembers", BindingFlags.Instance | BindingFlags.NonPublic) is not null);

    private static (Type Element, bool Many) Unwrap(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } inner)
            return (inner, false);
        if (type.IsArray)
            return (type.GetElementType()!, true);
        if (type != typeof(string) && type.IsGenericType
            && type.GetInterfaces().Append(type).Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            && type.GetGenericArguments().Length == 1)
            return (type.GetGenericArguments()[0], true);
        return (type, false);
    }

    private static string TypeName(PropertyInfo property)
    {
        var name = TypeName(property.PropertyType);
        var nullableReference = !property.PropertyType.IsValueType
            && Nullability.Create(property).ReadState == NullabilityState.Nullable;
        return nullableReference ? name + "?" : name;
    }

    private static string TypeName(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } inner)
            return TypeName(inner) + "?";
        if (type.IsArray)
            return TypeName(type.GetElementType()!) + "[]";

        if (type.IsGenericType)
        {
            var arguments = type.GetGenericArguments();
            var definition = type.GetGenericTypeDefinition();

            if (arguments.Length == 2 && definition.Name.Contains("Dictionary", StringComparison.Ordinal))
                return $"Map~{TypeName(arguments[0])}, {TypeName(arguments[1])}~";
            if (arguments.Length == 1 && Unwrap(type).Many)
                return TypeName(arguments[0]) + "[]";

            var bare = definition.Name[..definition.Name.IndexOf('`')].Replace("ValueTuple", "Tuple", StringComparison.Ordinal);
            return $"{bare}~{string.Join(", ", arguments.Select(TypeName))}~";
        }

        return type.FullName switch
        {
            "System.String" => "string",
            "System.Int32" => "int",
            "System.Int64" => "long",
            "System.Decimal" => "decimal",
            "System.Double" => "double",
            "System.Boolean" => "bool",
            _ => type.Name,
        };
    }
}
