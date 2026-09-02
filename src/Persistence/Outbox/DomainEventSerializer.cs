using System.Collections.Concurrent;
using System.Text.Json;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Persistence.Outbox;

/// <summary>
/// A domain event is a record: System.Text.Json is enough and the payload stays
/// readable in the table. The type name is stored without a version on purpose —
/// a pending row has to remain processable after a deployment.
/// </summary>
public static class DomainEventSerializer
{
    private static readonly ConcurrentDictionary<string, Type> ResolvedTypes = new();

    public static string TypeNameOf(IDomainEvent domainEvent)
    {
        var type = domainEvent.GetType();
        return $"{type.FullName}, {type.Assembly.GetName().Name}";
    }

    public static string Serialize(IDomainEvent domainEvent) =>
        JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), Jsonb.Options);

    public static IDomainEvent Deserialize(string typeName, string payload)
    {
        var type = ResolvedTypes.GetOrAdd(typeName, static name =>
            Type.GetType(name) ?? throw new InvalidOperationException(
                $"Outbox message type '{name}' could not be resolved. " +
                "Has the event been renamed or moved to another assembly?"));

        return (IDomainEvent)JsonSerializer.Deserialize(payload, type, Jsonb.Options)!;
    }
}
