namespace ElGuerre.Tendero.Search.Contracts;

/// <summary>
/// An AI stage this process was started without — not a stage that is failing.
///
/// The difference is what keeps <c>tendero.search.degraded</c> meaning something:
/// on a machine with no GPU and no vector layer every search would otherwise be
/// counted as degraded, and a counter that is always high says nothing on the day
/// Qdrant really goes down. A stage that is not configured is skipped, tagged on
/// the span, and never counted or logged as a warning.
/// </summary>
public sealed class SearchStageNotConfiguredException(string stage)
    : Exception($"This process was started without the {stage} stage.")
{
    public string Stage { get; } = stage;
}
