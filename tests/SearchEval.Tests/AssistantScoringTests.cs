using System.Text.Json;
using ElGuerre.Tendero.Assist.Tino;
using ElGuerre.Tendero.SearchEval.Assistant;
using ElGuerre.Tendero.SearchEval.Copilot;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.SearchEval.Tests;

/// <summary>
/// How a case of Tino's golden set is judged (phase 12b, T-5): every part compared,
/// none judged by a model. What a verdict says of a model is only as good as this.
/// </summary>
public sealed class AssistantScoringTests
{
    private static readonly Dictionary<string, string> Codes = new() { ["Iron pan"] = "PAN-IRON", ["Steel pan"] = "PAN-STEEL", ["Mug"] = "MUG-1" };

    private static readonly Dictionary<string, string> Orders = new() { ["ana-delivered"] = "id-ana", ["luis-delivered"] = "id-luis" };

    private static readonly TurnMeasure Measure = new("Answered", 4, 3000, 200, 0, 4.2, 1500, null);

    private static Reason Says(string text, string fact) => new(text, FactSource.DataSheet, fact, [], null);

    private static TinoAnswer Answer(
        TinoKind kind, AnsweredProduct[]? products = null, Reason[]? says = null, string? ask = null, NoData? noData = null,
        AnsweredOrder? order = null, int dropped = 0) =>
        new(TinoStatus.Answered, null, kind, products ?? [], says ?? [], ask, noData, [], "asked", dropped, null, "evaluated", false, "conv", order);

    private static AssistantVerdict Judge(Expectation expect, TinoAnswer? answer, params string[] reads) =>
        AssistantScoring.Judge(new AssistantCase("case", "es", "asked", expect), answer,
            [.. reads.Select(name => new ToolCall(name, JsonDocument.Parse("{}").RootElement))], Codes, Orders, Measure);

    private static TinoAnswer ThePan(string says = "Es apta para inducción.", string fact = "inducción: sí") =>
        Answer(TinoKind.Products, [new AnsweredProduct("PAN-IRON", "Sartén de hierro", [Says(says, fact)])]);

    [Fact]
    public void An_answer_with_everything_a_case_asks_for_passes_and_says_what_it_showed()
    {
        var verdict = Judge(
            new Expectation(["Products"], ["search_products"], ["my_orders"], ["Iron pan", "Steel pan"], ["Mug"],
                [new Citation("inducción", "Iron pan")], ["lavavajillas"], Language: "es"),
            ThePan() with { ReasonsDropped = 2 }, "search_products", "get_product_specs");

        Assert.True(verdict.Passed, string.Join("; ", verdict.Problems));
        Assert.Equal(("Answered", "Products", 1, 2), (verdict.Status, verdict.Kind, verdict.Shown, verdict.Dropped));
        Assert.Equal(["Es apta para inducción."], verdict.Sentences);
    }

    [Fact]
    public void The_kind_is_one_of_those_a_case_allows()
    {
        Assert.Equal(["answered Products, not NoData or Ask"], Judge(new Expectation(["NoData", "Ask"]), ThePan()).Problems);
        Assert.True(Judge(new Expectation(["nodata", "products"]), ThePan()).Passed);
    }

    [Fact]
    public void A_read_that_was_asked_for_and_not_made_or_made_and_not_wanted_is_a_problem()
    {
        var verdict = Judge(new Expectation(Tools: ["search_products", "get_return_policy"], NoTools: ["my_orders"]), ThePan(), "search_products", "my_orders");

        Assert.Equal(["did not read get_return_policy", "read my_orders"], verdict.Problems);
    }

    [Fact]
    public void A_product_is_compared_by_its_code_one_of_those_wanted_and_none_of_those_not()
    {
        Assert.Equal(["named none of: Steel pan, Mug"], Judge(new Expectation(AnyProduct: ["Steel pan", "Mug"]), ThePan()).Problems);
        Assert.Equal(["named Iron pan"], Judge(new Expectation(NoProduct: ["Iron pan"]), ThePan()).Problems);

        // What a product's details do not say is about that product too.
        var missing = Answer(TinoKind.NoData, noData: new NoData("PAN-IRON", "Sartén de hierro", "La ficha no indica…", []));
        Assert.True(Judge(new Expectation(AnyProduct: ["Iron pan"]), missing).Passed);
    }

    /// <summary>The fact is the shop's text, compared; the model's words are not what is looked at.</summary>
    [Fact]
    public void A_citation_is_the_fact_under_a_shown_sentence_not_the_sentences_own_words()
    {
        var wording = ThePan(says: "Funciona en placas de inducción.", fact: "material: hierro fundido");

        Assert.Equal(["cited no fact with \"inducción\""], Judge(new Expectation(Cites: [new Citation("inducción")]), wording).Problems);
        Assert.True(Judge(new Expectation(Cites: [new Citation("HIERRO fundido")]), wording).Passed);
        // And of the product named, not of another the answer also shows.
        Assert.Equal(["cited no fact with \"hierro\" of Steel pan"],
            Judge(new Expectation(Cites: [new Citation("hierro", "Steel pan")]), wording).Problems);
    }

    [Fact]
    public void A_fact_of_the_shop_or_an_order_or_the_nearest_of_a_sin_dato_can_be_what_is_cited()
    {
        var policy = Answer(TinoKind.Shop, says: [new Reason("Tienes 14 días.", FactSource.Policy, "Las devoluciones se aceptan durante 14 días desde la entrega.", [], null)]);
        var nearest = Answer(TinoKind.NoData, noData: new NoData("PAN-IRON", "Sartén", "La ficha no indica…", [Says("Es de hierro.", "material: hierro fundido")]));

        Assert.True(Judge(new Expectation(Cites: [new Citation("14 días")]), policy).Passed);
        Assert.True(Judge(new Expectation(Cites: [new Citation("hierro fundido", "Iron pan")]), nearest).Passed);
    }

    /// <summary>Words no shown sentence may carry: what a model must not have stated, whatever it cited.</summary>
    [Fact]
    public void What_must_not_be_said_is_looked_for_in_every_sentence_the_shopper_is_shown()
    {
        var verdict = Judge(new Expectation(NotSays: ["lavavajillas", "horno"]), ThePan(says: "Sí, es apta para LAVAVAJILLAS."));

        Assert.Equal(["said \"lavavajillas\""], verdict.Problems);
    }

    [Fact]
    public void An_order_is_compared_by_its_id_and_an_order_that_must_not_be_answered_about_never_is()
    {
        var aboutAnas = Answer(TinoKind.Order, says: [Says("Está entregado.", "Su estado es: entregado.")], order: new AnsweredOrder("id-ana", "Pedido"));

        Assert.True(Judge(new Expectation(Order: "ana-delivered", NotOrder: "luis-delivered"), aboutAnas).Passed);
        Assert.Equal(["not about the order luis-delivered"], Judge(new Expectation(Order: "luis-delivered"), aboutAnas).Problems);
        Assert.Equal(["about the order ana-delivered"], Judge(new Expectation(NotOrder: "ana-delivered"), aboutAnas).Problems);
        Assert.Equal(["not about the order ana-delivered"], Judge(new Expectation(Order: "ana-delivered"), ThePan()).Problems);
    }

    [Fact]
    public void The_language_is_that_of_the_models_own_sentences_and_a_kind_with_none_has_none_to_judge()
    {
        Assert.Equal(["written in es, not en"], Judge(new Expectation(Language: "en"), ThePan(says: "Esta sartén es apta para las placas de inducción y para el horno.")).Problems);
        Assert.True(Judge(new Expectation(Language: "en"), Answer(TinoKind.Ask, ask: "What will you cook with the pan and how many are you?")).Passed);
        Assert.True(Judge(new Expectation(Language: "en"), Answer(TinoKind.NoData, noData: new NoData(null, null, "No tenemos ese dato.", []))).Passed);
    }

    [Fact]
    public void No_answer_is_a_problem_with_its_cause_and_nothing_else_is_judged_but_the_reads()
    {
        var unavailable = new TinoAnswer(TinoStatus.Unavailable, TinoCause.Time, null, [], [], null, null, [], "asked", 0, null, null, false, "conv");

        var verdict = Judge(new Expectation(["Products"], ["search_products"], AnyProduct: ["Iron pan"]), unavailable);

        Assert.Equal(["did not read search_products", "did not answer: Time"], verdict.Problems);
        Assert.Equal(("Unavailable", "Time", (string?)null), (verdict.Status, verdict.Cause, verdict.Kind));
        Assert.Equal(["did not answer: no answer"], Judge(new Expectation(), null).Problems);
    }

    // ---------- the suite's own mistakes ----------

    [Fact]
    public void A_case_that_names_what_the_laboratory_does_not_have_is_the_suites_mistake_found_before_a_model_is_asked()
    {
        var people = new LaboratoryPeople(new Dictionary<string, CommercePrincipal> { ["anonymous"] = CommercePrincipal.Anonymous }, Orders);
        var cases = new AssistantCases(
        [
            new("fine", "es", "q", new Expectation(["Products"], ["search_products"], AnyProduct: ["Iron pan"], Order: "ana-delivered")),
            new("wrong", "es", "q",
                new Expectation(["Essay"], ["read_minds"], ["my_orders"], ["Gold pan"], Cites: [new Citation("x", "Silver pan")], NotOrder: "no-such-order"),
                As: "nobody-we-know", Context: new CaseContext(Product: "Copper pan")),
        ]);

        var unknown = AssistantScoring.Unknown(cases, Codes, people, ["search_products", "my_orders"]);

        Assert.Equal(
            [
                "wrong: product \"Gold pan\"", "wrong: product \"Silver pan\"", "wrong: product \"Copper pan\"", "wrong: order \"no-such-order\"",
                "wrong: read \"read_minds\"", "wrong: asker \"nobody-we-know\"", "wrong: kind \"Essay\"",
            ],
            unknown);
    }

    /// <summary>The committed cases are read as the suite reads them, and every case expects something.</summary>
    [Fact]
    public void The_committed_cases_read_and_each_expects_something()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "SearchEval", "Assistant", "cases.json");
        var cases = JsonSerializer.Deserialize<AssistantCases>(File.ReadAllText(path), CopilotScoring.Json)!;

        Assert.True(cases.Cases.Count >= 30);
        Assert.Equal(cases.Cases.Count, cases.Cases.Select(item => item.Id).Distinct().Count());
        Assert.All(cases.Cases, item => Assert.True(
            item.Expect is { Kinds.Count: > 0 } or { NotOrder: not null } or { NotSays.Count: > 0 }, item.Id));
        // The isolation the spec names is among them: an order asked for by who may not read it.
        Assert.Contains(cases.Cases, item => item is { As: "luis", Context.Order: "ana-delivered", Expect.NotOrder: "ana-delivered" });
        Assert.Contains(cases.Cases, item => item is { As: "anonymous", Context.Order: "ana-delivered", Expect.NotOrder: "ana-delivered" });
    }

    // ---------- the summary ----------

    [Fact]
    public void The_score_counts_what_passed_what_answered_why_not_and_how_long_it_took()
    {
        AssistantVerdict Verdict(bool passed, string status, string? cause, double seconds, int shown, int dropped, long largest) =>
            new("id", passed, [], status, cause, null, shown, dropped, Measure with { Seconds = seconds, LargestCall = largest }, []);

        var score = AssistantScoring.Summarise(
        [
            Verdict(true, "Answered", null, 2.0, 3, 1, 1000),
            Verdict(false, "Answered", null, 4.0, 2, 0, 3000),
            Verdict(false, "Unavailable", "Time", 20.0, 0, 0, 2000),
            Verdict(false, "Unavailable", "Time", 21.0, 0, 0, 2000),
        ]);

        Assert.Equal((4, 1, 2, 5, 1), (score.Cases, score.Passed, score.Answered, score.Shown, score.Dropped));
        Assert.Equal(2, score.Causes["Time"]);
        Assert.Equal((4.0, 21.0, 21.0, 3000L), (score.SecondsP50, score.SecondsP95, score.SecondsMax, score.LargestCall));
        Assert.StartsWith("local: 1/4 passed · 2 answered (2 Time) · 5 sentences shown, 1 dropped · p50 4.0s p95 21.0s", AssistantScoring.Line("local", score));
    }

    [Theory]
    [InlineData(0.50, 3.0)]
    [InlineData(0.95, 5.0)]
    [InlineData(0.20, 1.0)]
    [InlineData(1.00, 5.0)]
    public void A_percentile_is_the_value_that_share_of_the_turns_were_at_or_under(double share, double expected) =>
        Assert.Equal(expected, AssistantScoring.Percentile([1.0, 2.0, 3.0, 4.0, 5.0], share));

    [Fact]
    public void No_turns_have_no_percentile_and_no_score_to_divide() =>
        Assert.Equal((0, 0.0, 0L), (AssistantScoring.Summarise([]).Cases, AssistantScoring.Percentile([], 0.5), AssistantScoring.Summarise([]).LargestCall));
}
