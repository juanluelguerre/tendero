namespace ElGuerre.Tendero.SearchEval.Suites;

/// <summary>
/// An executable quality gate. There is one today — search relevance — and this
/// abstraction adds no capability: it exists so the next ones are cheap.
///
/// What is generalised is what cost dearly and is not specific to search: the
/// annotated set's format, the committed thresholds, the Markdown report and the
/// exit code. The two suites that are coming — extraction of claims with source
/// and confidence, and the actions the copilot proposes — use exactly that
/// machinery against a different kind of data.
///
/// The order matters: this enters BEFORE hybrid search. If it arrived
/// afterwards, the hybrid one would be measured against a baseline that can no
/// longer be compared with the committed one.
/// </summary>
public interface IEvaluationSuite
{
    /// <summary>The name passed in <c>--suite</c>.</summary>
    string Name { get; }

    /// <summary>What it measures, for the help message.</summary>
    string Description { get; }

    /// <summary>
    /// 0 when it passes, 1 when it falls below the thresholds and <c>--ci</c> was
    /// asked for, 2 when it could not even run (a dependency is not answering).
    /// </summary>
    Task<int> RunAsync(EvaluationOptions options, CancellationToken cancellationToken);
}
