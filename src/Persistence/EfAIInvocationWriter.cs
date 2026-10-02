using ElGuerre.Tendero.AI;
using Microsoft.EntityFrameworkCore;

namespace ElGuerre.Tendero.Persistence;

/// <summary>On a context of its own, as the audit writer: the rows are no part of
/// any request's transaction.</summary>
internal sealed class EfAIInvocationWriter(IDbContextFactory<TenderoDbContext> contexts) : IAIInvocationWriter
{
    public async Task WriteAsync(IReadOnlyList<AIInvocation> rows, CancellationToken cancellationToken = default)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        context.AIInvocations.AddRange(rows);
        await context.SaveChangesAsync(cancellationToken);
    }
}
