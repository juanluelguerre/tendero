namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// Every feature flag the system has, as a closed set (ADR 0023).
///
/// **A flag guards behaviour that exists and can be turned on** — article 07's
/// rule, and the reason this enum has two members and not the five the roadmap
/// listed. <c>SearchMultimodal</c>, <c>UcpTransactional</c> and <c>Copilot</c>
/// would each guard code that is not written yet, and a flag over nothing is
/// configuration that promises a feature. Each enters with the phase that builds
/// what it guards.
///
/// Two rules keep the set honest, and a test holds both: every member says, in
/// <see cref="RemovalConditionAttribute"/>, the measurable condition under which
/// it is deleted, and every member is read in exactly ONE place in production
/// code. A flag read in two places is two decisions that can disagree; a flag read
/// in none is dead configuration.
/// </summary>
public enum FeatureId
{
    [RemovalCondition(
        "Deleted when hybrid search clears its own committed thresholds AND beats lexical on NDCG@10 " +
        "in both cultures in three consecutive gate runs. The storefront then always asks for hybrid, " +
        "and lexical stays what it already is: the mode hybrid degrades to.")]
    SearchVector,

    [RemovalCondition(
        "Deleted when the reranker is measured to raise hybrid's NDCG@10 in both cultures AND to answer " +
        "inside its budget on the CPU as well as on a GPU — or deleted together with the reranker, " +
        "if the gate shows it does not earn its latency.")]
    SearchRerank
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
