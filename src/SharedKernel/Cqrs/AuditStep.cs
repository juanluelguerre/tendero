using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// The pipeline's second step, and the first one that writes.
///
/// It runs around COMMANDS only. The roadmap put it here rather than in the
/// handlers for the reason every cross-cutting concern ends up in a pipeline: a
/// handler that has to remember to audit is a handler that will one day forget,
/// and the forgetting is silent.
///
/// **What it records is the outcome, including the ones that are not errors.**
/// A denial is the system working — a rule said no — and it is the row phase
/// 11's agent activity panel is built from. Filing it under "failed" would make
/// that panel unbuildable, so <see cref="AuditOutcome"/> tells the three apart.
///
/// It never breaks the command it is watching. An audit writer that threw would
/// turn a successful checkout into a 500 over bookkeeping, so a failure here is
/// swallowed and tagged on the span instead — the one place in this repository
/// where swallowing an exception is right, and it says so.
/// </summary>
internal static class AuditStep
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.SharedKernel);

    /// <summary>What a redacted value is replaced with. A marker and not a
    /// removal, so the row still shows the field was there.</summary>
    private const string Redacted = "[redacted]";

    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Sensitive = new();

    private static readonly JsonSerializerOptions Payloads = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Strongly-typed ids are record structs and serialise as {"value":"…"},
        // which is noise in a log a person reads. The converters that flatten
        // them on the wire are the API's, not the kernel's, so the payload keeps
        // the shape the command actually had — honest, if verbose.
        WriteIndented = false
    };

    /// <summary>
    /// Runs the command and records what happened, whichever way it went.
    ///
    /// The entry is written AFTER the handler returns rather than before,
    /// because "somebody tried" and "somebody did" are different facts and only
    /// the second one is worth a row. A pre-write would need a second write to
    /// close it out, which is a two-phase log for a single-writer table.
    /// </summary>
    public static async Task<TResult> RecordAsync<TCommand, TResult>(
        TCommand command,
        IServiceProvider services,
        CancellationToken ct,
        Func<Task<TResult>> handle)
    {
        var writer = services.GetService<IAuditWriter>();

        // No writer registered means auditing is not wired in this process —
        // the SearchEval tool composes its own container and has no database.
        // It runs the command and says nothing, rather than refusing to start.
        if (writer is null)
            return await handle();

        var principal = services.GetService<IPrincipalAccessor>()?.Current
                        ?? CommercePrincipal.Anonymous;

        var clock = services.GetService<TimeProvider>() ?? TimeProvider.System;
        var traceId = Activity.Current?.TraceId.ToString();

        try
        {
            var result = await handle();

            await SafelyWriteAsync(writer, AuditEntry.For(
                typeof(TCommand).Name,
                Payload(command),
                principal,
                // A command that returned normally was allowed. A handler that
                // wants to record a REFUSAL — "the mandate does not cover this"
                // — throws, or phase 11's policy decorator denies before it ever
                // reaches here. Neither case is a success returning quietly.
                AuditOutcome.Allowed,
                reason: null,
                traceId,
                clock.GetUtcNow()), ct);

            return result;
        }
        catch (Exception failure)
        {
            await SafelyWriteAsync(writer, AuditEntry.For(
                typeof(TCommand).Name,
                Payload(command),
                principal,
                // An invariant the domain refused is a DENIAL, not a failure.
                // "Cannot publish an archived product" is the shop working, and
                // it belongs beside the agent denials rather than beside the
                // database being down.
                failure is InvalidOperationException or ArgumentException
                    ? AuditOutcome.Denied
                    : AuditOutcome.Failed,
                failure.Message,
                traceId,
                clock.GetUtcNow()), ct);

            throw;
        }
    }

    /// <summary>
    /// The one place in this repository where an exception is swallowed on
    /// purpose.
    ///
    /// An audit writer that threw would turn a completed checkout into a 500
    /// over bookkeeping — the command already happened, and failing the request
    /// now would tell the shopper their order did not go through when it did.
    /// The loss is recorded on the span so it is visible in a trace rather than
    /// nowhere.
    /// </summary>
    private static async Task SafelyWriteAsync(
        IAuditWriter writer, AuditEntry entry, CancellationToken ct)
    {
        try
        {
            await writer.WriteAsync(entry, ct);
        }
        catch (Exception lost)
        {
            using var activity = Telemetry.StartActivity("audit.lost");
            activity?.SetTag("audit.command", entry.CommandType);
            activity?.SetTag("audit.error", lost.Message);
        }
    }

    /// <summary>
    /// The command as JSON, with anything marked
    /// <see cref="AuditRedactedAttribute"/> replaced.
    ///
    /// Redaction is not hypothetical here: checkout and the guest claim both
    /// carry a CART TOKEN, which is a 256-bit bearer credential. A log holding
    /// one is a log that can be used to take over somebody's basket, and an
    /// audit table is exactly the kind of thing that gets exported to a
    /// spreadsheet.
    /// </summary>
    private static string Payload(object? command)
    {
        if (command is null) return "{}";

        var redacted = Sensitive.GetOrAdd(command.GetType(), static type =>
            [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.IsDefined(typeof(AuditRedactedAttribute), inherit: true))]);

        if (redacted.Length == 0)
            return Serialise(command);

        var values = command.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead)
            .ToDictionary(
                property => property.Name,
                property => redacted.Contains(property)
                    ? Redacted
                    : property.GetValue(command));

        return Serialise(values);
    }

    /// <summary>
    /// A command that cannot be serialised must not take the command down with
    /// it. The type name is a poorer record than the payload and a much better
    /// one than a failed request.
    /// </summary>
    private static string Serialise(object value)
    {
        try
        {
            return JsonSerializer.Serialize(value, Payloads);
        }
        catch (NotSupportedException)
        {
            return $$"""{"unserialisable":"{{value.GetType().Name}}"}""";
        }
    }
}
