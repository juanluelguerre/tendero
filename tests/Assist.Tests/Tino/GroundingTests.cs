using ElGuerre.Tendero.AI.Support;
using ElGuerre.Tendero.Assist.Tino;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Assist.Tests.Tino;

/// <summary>
/// A sentence held to the fact it cites (the Tino spec, *Grounding*): its figures
/// compared, its meaning scored. The scorer here answers from a table, so what is
/// tested is what code does with a score — the scores themselves are measured
/// (<c>docs/analysis/tino-models.md</c>).
/// </summary>
public sealed class GroundingTests
{
    /// <summary>Scores a sentence by what it is, whatever the premise — and keeps every pair it was asked.</summary>
    internal sealed class Scorer(Func<string, string, double> score) : ISupportScorer
    {
        public List<(string Premise, string Sentence)> Asked { get; } = [];

        public static Scorer Saying(params string[] held) => new((_, sentence) => held.Contains(sentence) ? 0.95 : 0.05);

        public Task<double> ScoreAsync(string text, string claim, CancellationToken cancellationToken)
        {
            Asked.Add((text, claim));
            return Task.FromResult(score(text, claim));
        }
    }

    private static Reason About(string says, string fact, FactSource source = FactSource.DataSheet) => new(says, source, fact, [], null);

    private static TinoBody Products(params AnsweredProduct[] products) => new(TinoKind.Products, products, [], null, null, 0);

    private static Task<TinoBody> Hold(TinoBody body, Scorer scorer, FactSheet? sheet = null, double threshold = 0.5) =>
        new Grounding(scorer, threshold).HoldAsync(body, sheet ?? new FactSheet(), TestContext.Current.CancellationToken);

    // ---------- the figures ----------

    [Theory]
    [InlineData("Pesa 0,8 kg.", "Peso: 0.8 kg", true)]
    [InlineData("Pesa 0.80 kg.", "Peso: 0,8 kg", true)]
    [InlineData("It fits laptops up to 16 inches.", "laptop up to: 16", true)]
    [InlineData("Sus diámetros son 20, 24 y 28 cm.", "diámetros: 20, 24, 28", true)]
    [InlineData("Es apta para inducción.", "inducción: sí", true)]
    [InlineData("It is induction-ready and holds 1 L.", "induction: yes", false)]
    [InlineData("Pesa 0,9 kg.", "Peso: 0.8 kg", false)]
    [InlineData("Mide 26 cm.", "diámetros: 20, 24, 28", false)]
    [InlineData("Tiene 30 días.", "", false)]
    [InlineData("", "Peso: 0.8 kg", true)]
    public void Every_figure_of_a_sentence_is_a_figure_of_what_it_rests_on(string sentence, string premise, bool all) =>
        Assert.Equal(all, Figures.AllIn(sentence, premise));

    // ---------- the reasons ----------

    [Fact]
    public async Task A_reason_its_fact_says_stands_and_one_it_does_not_say_is_dropped_and_counted()
    {
        var scorer = Scorer.Saying("Está hecho de cristal.");

        var held = await Hold(Products(new AnsweredProduct("JAR-1", "Botes de cocina herméticos",
        [
            About("Está hecho de cristal.", "material: cristal"),
            About("Está hecho de acero inoxidable.", "piezas: 3"),
        ])), scorer);

        Assert.Equal(["Está hecho de cristal."], held.Products.Single().Reasons.Select(reason => reason.Text));
        Assert.Equal(1, held.Dropped);
    }

    /// <summary>The fact alone does not entail a sentence that names the product; the name and the fact do.</summary>
    [Fact]
    public async Task What_a_reason_is_held_to_is_the_products_name_and_the_fact()
    {
        var scorer = Scorer.Saying("Es un termómetro de sonda de acero inoxidable.");

        await Hold(Products(new AnsweredProduct("TH-1", "Termómetro de cocina de sonda",
            [About("Es un termómetro de sonda de acero inoxidable.", "material: acero inoxidable")])), scorer);

        Assert.Equal(("Termómetro de cocina de sonda. material: acero inoxidable", "Es un termómetro de sonda de acero inoxidable."),
            Assert.Single(scorer.Asked));
    }

    /// <summary>A number is compared, not scored: the scorer is not even asked about a sentence with a figure of its own.</summary>
    [Fact]
    public async Task A_figure_the_fact_does_not_state_drops_the_reason_without_asking_the_scorer()
    {
        var scorer = new Scorer((_, _) => 1.0);

        var held = await Hold(Products(new AnsweredProduct("PAN-1", "Cazo de 16 cm",
        [
            About("It is induction-ready and holds 1 L.", "induction: yes"),
            // The product's name is part of what a reason rests on: its 16 is the name's.
            About("Es un cazo de 16 cm apto para inducción.", "induction: yes"),
        ])), scorer);

        Assert.Equal(["Es un cazo de 16 cm apto para inducción."], held.Products.Single().Reasons.Select(reason => reason.Text));
        Assert.Equal(1, held.Dropped);
        Assert.Equal("Es un cazo de 16 cm apto para inducción.", Assert.Single(scorer.Asked).Sentence);
    }

    [Theory]
    [InlineData(0.5, true)]
    [InlineData(0.49, false)]
    public async Task A_score_at_the_threshold_stands_and_one_under_it_does_not(double score, bool stands)
    {
        var held = await Hold(Products(new AnsweredProduct("P-1", "Producto", [About("Dice algo.", "dato")])), new Scorer((_, _) => score));

        Assert.Equal(stands ? 1 : 0, held.Products.Single().Reasons.Count);
        Assert.Equal(stands ? 0 : 1, held.Dropped);
    }

    /// <summary>The search found it, so it is shown — as a plain card, with nothing said of it.</summary>
    [Fact]
    public async Task A_product_left_with_no_reason_is_still_named_and_what_was_dropped_before_is_kept_in_the_count()
    {
        var body = Products(new AnsweredProduct("P-1", "Producto", [About("Es el mejor.", "dato")])) with { Dropped = 2 };

        var held = await Hold(body, Scorer.Saying());

        Assert.Equal(("P-1", 0), (held.Products.Single().Code, held.Products.Single().Reasons.Count));
        Assert.Equal(3, held.Dropped);
    }

    [Fact]
    public async Task What_is_said_of_the_shop_is_held_to_the_shops_fact_alone()
    {
        var scorer = Scorer.Saying("Tienes 14 días desde la entrega.");
        var policy = "Las devoluciones se aceptan durante 14 días desde la entrega.";

        var held = await Hold(new TinoBody(TinoKind.Shop, [],
            [About("Tienes 14 días desde la entrega.", policy, FactSource.Policy), About("Tienes 30 días.", policy, FactSource.Policy)],
            null, null, 0), scorer);

        Assert.Equal(["Tienes 14 días desde la entrega."], held.Says.Select(reason => reason.Text));
        Assert.Equal(1, held.Dropped);
        // No product, no name: the premise is the fact.
        Assert.Equal(policy, Assert.Single(scorer.Asked).Premise);
    }

    [Fact]
    public async Task A_question_back_is_not_a_statement_and_nothing_is_asked_of_the_scorer()
    {
        var scorer = new Scorer((_, _) => 0);
        var ask = new TinoBody(TinoKind.Ask, [], [], "¿Para qué la quieres?", null, 0);

        Assert.Same(ask, await Hold(ask, scorer));
        Assert.Empty(scorer.Asked);
    }

    // ---------- "the details do not say" ----------

    private static FactSheet Pan()
    {
        var sheet = new FactSheet();
        sheet.Add(new ReadAnswer(new { }, 2, null,
            Facts:
            [
                new Fact("PAN-1", FactSource.DataSheet, "material: hierro fundido"),
                new Fact("PAN-1", FactSource.Description, "Apta para inducción, horno y fuego directo."),
                new Fact("PAN-2", FactSource.DataSheet, "Apta para horno: sí"),
            ],
            Subjects: [new Subject("PAN-1", "Sartén de hierro"), new Subject("PAN-2", "Sartén de acero")]));
        return sheet;
    }

    private static TinoBody NoData(string code, string? claim, params Reason[] nearest) =>
        new(TinoKind.NoData, [], [], null,
            new NoData(code, "Sartén de hierro", "La ficha de este producto no indica si puede ir al horno.", nearest), 0, claim);

    /// <summary>The model said the details do not say it; one of them does. The answer is that fact, not "sin dato".</summary>
    [Fact]
    public async Task What_the_details_do_say_is_not_missing_it_is_the_answer_with_the_fact_that_says_it()
    {
        var scorer = new Scorer((premise, sentence) => sentence == "Puede ir al horno." && premise.Contains("horno") ? 0.97 : 0.02);

        var held = await Hold(NoData("PAN-1", "Puede ir al horno."), scorer, Pan());

        Assert.Equal(TinoKind.Products, held.Kind);
        Assert.Null(held.NoData);
        var product = held.Products.Single();
        Assert.Equal(("PAN-1", "Sartén de hierro"), (product.Code, product.Name));
        Assert.Equal(("Puede ir al horno.", FactSource.Description, "Apta para inducción, horno y fuego directo."),
            (product.Reasons.Single().Text, product.Reasons.Single().Source, product.Reasons.Single().Fact));
        // Only that product's facts were asked about: the other pan's oven is not this one's.
        Assert.All(scorer.Asked, asked => Assert.StartsWith("Sartén de hierro. ", asked.Premise));
        Assert.Equal(2, scorer.Asked.Count);
    }

    [Fact]
    public async Task What_no_detail_says_stays_missing_with_the_nearest_facts_that_hold()
    {
        var scorer = Scorer.Saying("Es de hierro fundido.");

        var held = await Hold(
            NoData("PAN-1", "Puede ir al lavavajillas.",
                About("Es de hierro fundido.", "material: hierro fundido"), About("Aguanta todo.", "material: hierro fundido")),
            scorer, Pan());

        Assert.Equal(TinoKind.NoData, held.Kind);
        Assert.Equal("La ficha de este producto no indica si puede ir al horno.", held.NoData!.Sentence);
        Assert.Equal(["Es de hierro fundido."], held.NoData.Nearest.Select(reason => reason.Text));
        Assert.Equal(1, held.Dropped);
    }

    /// <summary>A claim with a figure no fact states is not said by any of them, whatever the scorer thinks.</summary>
    [Fact]
    public async Task A_claim_is_held_to_the_figures_too_and_with_no_claim_nothing_is_confirmed()
    {
        var scorer = new Scorer((_, _) => 1.0);

        Assert.Equal(TinoKind.NoData, (await Hold(NoData("PAN-1", "Aguanta 250 grados."), scorer, Pan())).Kind);
        Assert.Empty(scorer.Asked);
        Assert.Equal(TinoKind.NoData, (await Hold(NoData("PAN-1", null), scorer, Pan())).Kind);
        Assert.Empty(scorer.Asked);
    }

    [Fact]
    public async Task What_the_shop_does_not_hold_names_no_product_and_is_not_asked_about()
    {
        var scorer = new Scorer((_, _) => 1.0);
        var unknown = new TinoBody(TinoKind.NoData, [], [], null, new NoData(null, null, "No tenemos ese dato.", []), 0);

        Assert.Same(unknown, await Hold(unknown, scorer, Pan()));
        Assert.Empty(scorer.Asked);
    }

    // ---------- the scorer itself ----------

    [Fact]
    public async Task A_scorer_that_does_not_answer_is_said_so_and_nothing_is_let_through()
    {
        var down = new Scorer((_, _) => throw new HttpRequestException("connection refused"));

        var failure = await Assert.ThrowsAsync<SupportUnavailableException>(() =>
            Hold(Products(new AnsweredProduct("P-1", "Producto", [About("Dice algo.", "dato")])), down));

        Assert.IsType<HttpRequestException>(failure.InnerException);
    }
}
