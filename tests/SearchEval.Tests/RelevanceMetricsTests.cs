using Xunit;

namespace ElGuerre.Tendero.SearchEval.Tests;

/// <summary>
/// Los valores esperados están calculados a mano, no capturados de la propia
/// implementación: una métrica que se compara consigo misma no prueba nada, y
/// esta es la que decide si un PR entra o no.
/// </summary>
public sealed class RelevanceMetricsTests
{
    private static readonly Dictionary<string, int> Judgments = new()
    {
        ["a"] = 3,
        ["b"] = 3,
        ["c"] = 2,
        ["d"] = 1,
        ["noise"] = 0
    };

    [Fact]
    public void A_perfect_ranking_scores_one()
    {
        var ndcg = RelevanceMetrics.NdcgAt(10, ["a", "b", "c", "d"], Judgments);

        Assert.Equal(1.0, ndcg!.Value, precision: 6);
    }

    [Fact]
    public void The_ideal_order_is_the_same_documents_sorted_by_relevance()
    {
        // Mismos documentos, orden distinto: recall no cambia, NDCG sí. Esa es
        // toda la razón de medir las dos cosas.
        var scrambled = RelevanceMetrics.NdcgAt(10, ["d", "noise", "a", "b", "c"], Judgments);

        // DCG = 1/1 + 0/1.585 + 7/2 + 7/2.322 + 3/2.585 = 1 + 3.5 + 3.0146 + 1.1605 = 8.6751
        // IDCG = 7/1 + 7/1.585 + 3/2 + 1/2.322 = 7 + 4.4165 + 1.5 + 0.4307 = 13.3472
        Assert.Equal(0.6499, scrambled!.Value, precision: 3);
        Assert.Equal(1.0, RelevanceMetrics.RecallAt(50, ["d", "noise", "a", "b", "c"], Judgments)!.Value);
    }

    [Fact]
    public void Relevance_grows_faster_than_linearly()
    {
        // Un resultado perfecto (3) arriba vale más que dos tangenciales (1).
        var oneExcellent = RelevanceMetrics.NdcgAt(10, ["a"], Judgments)!.Value;
        var twoWeak = RelevanceMetrics.NdcgAt(10, ["d"], new Dictionary<string, int> { ["d"] = 1 })!.Value;

        Assert.True(oneExcellent < twoWeak,
            "un solo resultado excelente no puede llenar un ideal que tiene cuatro");
        Assert.Equal(7d / 13.3472, oneExcellent, precision: 3);
    }

    [Fact]
    public void Position_matters_the_ranking_is_not_a_set()
    {
        var top = RelevanceMetrics.NdcgAt(10, ["a", "noise", "noise"], Judgments)!.Value;
        var buried = RelevanceMetrics.NdcgAt(10, ["noise", "noise", "a"], Judgments)!.Value;

        Assert.True(top > buried, "el mismo acierto vale menos enterrado");
    }

    [Fact]
    public void The_cutoff_hides_what_falls_past_it()
    {
        IReadOnlyList<string> ranked = ["noise", "noise", "noise", "a"];

        Assert.Equal(0d, RelevanceMetrics.NdcgAt(3, ranked, Judgments)!.Value);
        Assert.True(RelevanceMetrics.NdcgAt(10, ranked, Judgments)!.Value > 0d);
    }

    [Fact]
    public void Recall_counts_what_was_found_regardless_of_where()
    {
        // Cuatro relevantes (a, b, c, d); "noise" tiene relevancia 0 y no cuenta.
        Assert.Equal(0.5, RelevanceMetrics.RecallAt(50, ["b", "noise", "c"], Judgments)!.Value);
    }

    [Fact]
    public void A_query_with_nothing_relevant_annotated_does_not_score()
    {
        var onlyNoise = new Dictionary<string, int> { ["noise"] = 0 };

        // null y no 0: castigar al motor por una anotación incompleta falsearía la media.
        Assert.Null(RelevanceMetrics.NdcgAt(10, ["noise"], onlyNoise));
        Assert.Null(RelevanceMetrics.RecallAt(50, ["noise"], onlyNoise));
    }

    [Fact]
    public void An_empty_result_page_scores_zero_not_null()
    {
        Assert.Equal(0d, RelevanceMetrics.NdcgAt(10, [], Judgments)!.Value);
        Assert.Equal(0d, RelevanceMetrics.RecallAt(50, [], Judgments)!.Value);
    }
}
