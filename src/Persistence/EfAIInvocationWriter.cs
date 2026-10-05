using ElGuerre.Tendero.AI;
using Microsoft.EntityFrameworkCore;

namespace ElGuerre.Tendero.Persistence;

/// <summary>On a context of its own, as the audit writer: the rows are no part of
/// any request's transaction.</summary>
internal sealed class EfAIInvocationWriter(IDbContextFactory<TenderoDbContext> contexts) : IAIInvocationWriter, IAIInvocationReader
{
    public async Task<long> ConversationTokensAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        return await context.AIInvocations.AsNoTracking()
            .Where(row => row.ConversationId == conversationId)
            .SumAsync(row => row.InputTokens + row.OutputTokens, cancellationToken);
    }

    public async Task<long> PersonTokensSinceAsync(string subject, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        return await context.AIInvocations.AsNoTracking()
            .Where(row => row.Subject == subject && row.At >= since)
            .SumAsync(row => row.InputTokens + row.OutputTokens, cancellationToken);
    }

    public async Task<long> OperationTokensSinceAsync(string operation, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        return await context.AIInvocations.AsNoTracking()
            .Where(row => row.Operation == operation && row.At >= since)
            .SumAsync(row => row.InputTokens + row.OutputTokens, cancellationToken);
    }

    public async Task WriteAsync(IReadOnlyList<AIInvocation> rows, CancellationToken cancellationToken = default)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        context.AIInvocations.AddRange(rows);
        await context.SaveChangesAsync(cancellationToken);
    }
}
