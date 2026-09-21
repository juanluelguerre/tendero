namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// Every feature flag the system has, as a closed set (ADR 0023) — and today the
/// set is EMPTY, on purpose.
///
/// **A flag guards behaviour that exists and can be turned on** — the rule of the
/// article *The feature flag you did not need*, and the reason this enum never held the five the roadmap listed.
/// <c>SearchMultimodal</c>, <c>UcpTransactional</c> and <c>Copilot</c> would each
/// guard code that is not written yet, and a flag over nothing is configuration
/// that promises a feature. Each enters with the phase that builds what it guards.
///
/// The two that did exist both left, each for its own written reason (ADR 0023,
/// amended 2026-09-17):
///
/// <c>SearchRerank</c> became a deployment capability. The AppHost already decided
/// whether a reranker exists — it starts the container only where it can keep its
/// budget, a GPU today (ADR 0031) — so the flag was a second place recording a
/// decision made once, and the two could silently disagree.
/// <c>HybridSearchStrategy</c> reranks whenever <c>IReranker.IsConfigured</c> says
/// a real one was registered, and a process started without one
/// (<c>NoReranker</c>) is skipped and tagged <c>search.stage.unavailable</c> rather
/// than counted as degraded — the distinction
/// <c>SearchStageNotConfiguredException</c> carries.
///
/// <c>SearchVector</c> met its removal condition: hybrid search cleared its own
/// committed thresholds and beat lexical on NDCG@10 in both cultures in three
/// consecutive gate runs (2026-09-15 twice, 2026-09-17 on the GPU). The storefront
/// always asks for hybrid now, and lexical stays what it already was: the mode
/// hybrid degrades to.
///
/// Two rules keep the set honest, and a test holds both the day a member returns:
/// every member says, in <see cref="RemovalConditionAttribute"/>, the measurable
/// condition under which it is deleted, and every member is read in exactly ONE
/// place in production code. A flag read in two places is two decisions that can
/// disagree; a flag read in none is dead configuration.
/// </summary>
public enum FeatureId
{
}

/// <summary>The measurable condition under which a flag is deleted. Required on every <see cref="FeatureId"/>.</summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public sealed class RemovalConditionAttribute(string condition) : Attribute
{
    public string Condition { get; } = condition;
}

/// <summary>
/// Whether a flag is on. A port, because the answer is configuration and the
/// kernel knows nothing about where configuration lives; the composition root
/// registers the implementation.
/// </summary>
public interface IFeatureGate
{
    bool IsEnabled(FeatureId feature);
}
