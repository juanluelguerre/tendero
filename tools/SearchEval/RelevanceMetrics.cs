namespace Tendero.SearchEval;

/// <summary>
/// NDCG@k y recall@k, funciones puras sobre (ranking, juicios). Están aisladas
/// de Elasticsearch a propósito: una métrica mal implementada invalida la puerta
/// de CI en silencio, así que se prueba con valores calculados a mano.
/// </summary>
public static class RelevanceMetrics
{
    /// <summary>Relevancia mínima para considerar un resultado "relevante" en recall.</summary>
    private const int RelevantFrom = 1;

    /// <summary>
    /// Normalized Discounted Cumulative Gain. La ganancia crece como 2^rel - 1
    /// (un resultado perfecto vale mucho más que varios mediocres) y se descuenta
    /// por log2(posición + 1), porque nadie baja. Se normaliza contra el orden
    /// ideal, así que 1.0 significa "imposible ordenarlo mejor".
    /// </summary>
    /// <returns>null si la consulta no tiene ningún juicio relevante: NDCG no
    /// está definido ahí, y promediar un 0 falso castigaría al motor por una
    /// anotación incompleta.</returns>
    public static double? NdcgAt(
        int k, IReadOnlyList<string> rankedIds, IReadOnlyDictionary<string, int> judgments)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(k);

        var idealGains = judgments.Values
            .Where(relevance => relevance > 0)
            .OrderByDescending(relevance => relevance)
            .Take(k)
            .ToList();

        if (idealGains.Count == 0)
            return null;

        var actual = 0d;
        for (var position = 0; position < Math.Min(k, rankedIds.Count); position++)
        {
            var relevance = judgments.GetValueOrDefault(rankedIds[position], 0);
            actual += Gain(relevance) / Discount(position);
        }

        var ideal = 0d;
        for (var position = 0; position < idealGains.Count; position++)
            ideal += Gain(idealGains[position]) / Discount(position);

        return actual / ideal;
    }

    /// <summary>
    /// Proporción de lo relevante que aparece en los primeros k. Vigila la fase
    /// de recuperación con independencia del orden: si recall cae, el problema es
    /// que no se encuentran, no que estén mal colocados.
    /// </summary>
    public static double? RecallAt(
        int k, IReadOnlyList<string> rankedIds, IReadOnlyDictionary<string, int> judgments)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(k);

        var relevant = judgments
            .Where(judgment => judgment.Value >= RelevantFrom)
            .Select(judgment => judgment.Key)
            .ToHashSet(StringComparer.Ordinal);

        if (relevant.Count == 0)
            return null;

        var found = rankedIds.Take(k).Count(relevant.Contains);

        return (double)found / relevant.Count;
    }

    private static double Gain(int relevance) => Math.Pow(2, relevance) - 1;

    // position es 0-based; la primera posición no se descuenta (log2(2) = 1).
    private static double Discount(int position) => Math.Log2(position + 2);
}
