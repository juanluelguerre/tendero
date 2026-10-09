using System.Text.Json;
using ElGuerre.Tendero.SearchEval.Copilot;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.SearchEval.Tests;

/// <summary>
/// The copilot suite's verdicts (S-6): the right reads, with the arguments that
/// matter, and exactly the proposals expected — compared, never judged by a model.
/// </summary>
public sealed class CopilotScoringTests
{
    private static readonly TurnMeasure Measure = new("Answered", 2, 1800, 200, 0, 12.5, 1900, null);

    private static readonly IReadOnlyDictionary<string, string> Codes = new Dictionary<string, string>
    {
        ["Probe kitchen thermometer"] = "0B6K18J7PB",
        ["Pista padel shoes"] = "CJ0BBKTB5A",
    };

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static CopilotCase Case(IReadOnlyList<ExpectedTool> tools, IReadOnlyList<ExpectedProposal>? proposals = null) =>
        new("case", "question", "en", null, tools, proposals ?? []);

    [Fact]
    public void A_read_with_the_arguments_that_matter_passes_whatever_else_it_carries()
    {
        var verdict = CopilotScoring.Judge(
            Case([new ExpectedTool("catalogue_gaps", new Dictionary<string, string> { ["kind"] = "MissingName", ["culture"] = "en" })]),
            [new ToolCall("catalogue_gaps", Json("""{"kind":"missingname","culture":"EN","namesIn":"es"}"""))], [], Codes, Measure);

        Assert.True(verdict.Passed);
    }

    [Fact]
    public void The_right_tool_with_the_wrong_arguments_is_not_valid()
    {
        var verdict = CopilotScoring.Judge(
            Case([new ExpectedTool("sales_summary", new Dictionary<string, string> { ["period"] = "Month" })]),
            [new ToolCall("sales_summary", Json("""{"period":"Week"}"""))], [], Codes, Measure);

        Assert.Equal((true, false, true, false), (verdict.RightTools, verdict.ValidArguments, verdict.ExactProposals, verdict.Passed));
    }

    /// <summary>A model's words are matched by what they must contain: "~press" holds for "French press".</summary>
    [Fact]
    public void Free_text_is_matched_by_what_it_contains()
    {
        var verdict = CopilotScoring.Judge(
            Case([new ExpectedTool("find_products", new Dictionary<string, string> { ["text"] = "~press" })]),
            [new ToolCall("find_products", Json("""{"text":"French Press"}"""))], [], Codes, Measure);

        Assert.True(verdict.ValidArguments);
    }

    [Fact]
    public void A_proposal_is_matched_by_its_command_its_product_and_the_fields_that_matter()
    {
        var verdict = CopilotScoring.Judge(
            Case([], [new ExpectedProposal("SetProductTranslation", "Probe kitchen thermometer",
                new Dictionary<string, string> { ["culture"] = "en", ["name"] = "Kitchen probe thermometer" })]),
            [], [new StoredProposal("SetProductTranslation", "0B6K18J7PB", Json("""{"productId":"x","culture":"en","name":"kitchen probe thermometer "}"""))],
            Codes, Measure);

        Assert.True(verdict.ExactProposals);
    }

    /// <summary>A question that only asks must not propose: a proposal nobody asked for fails the case.</summary>
    /// <summary>A Spanish question under an English culture is answered in Spanish (P12-10): the case says so, and words decide.</summary>
    [Fact]
    public void An_answer_in_another_language_than_the_case_asks_for_fails_it()
    {
        var spanish = Case([]) with { AnswerLanguage = "es" };

        var right = CopilotScoring.Judge(spanish, [], [], Codes, Measure with { Answer = "Esta semana no hay ventas registradas en la tienda." });
        var wrong = CopilotScoring.Judge(spanish, [], [], Codes, Measure with { Answer = "There are no sales in the shop this week." });

        Assert.True(right.Passed);
        Assert.False(wrong.Passed);
        Assert.False(wrong.RightLanguage);
        Assert.Contains("answered in en, not es", Assert.Single(wrong.Problems));
    }

    /// <summary>A case that asks for no language is not judged on one, whatever the answer.</summary>
    [Fact]
    public void A_case_that_asks_for_no_language_is_not_judged_on_one()
    {
        var verdict = CopilotScoring.Judge(Case([]), [], [], Codes, Measure with { Answer = "Hecho." });

        Assert.True(verdict.Passed);
    }

    /// <summary>The product names an answer lists are the catalogue's language, not the answer's: only its opening is read.</summary>
    [Theory]
    [InlineData("There are 3 lamps in the catalogue:\n\n- Lámpara de escritorio con carga\n- Flexo de taller para el banco\n- Lámpara de la mesa de la sala", "en")]
    [InlineData("Hay 2 productos sin nombre en inglés.\n\n- The lamp of the desk\n- The bag for the gym", "es")]
    [InlineData("OK.", null)]
    [InlineData(null, null)]
    public void The_language_is_read_from_the_answers_opening(string? answer, string? expected)
    {
        Assert.Equal(expected, CopilotScoring.LanguageOf(answer));
    }

    // ---------- the dry run's script, held to the cases it rehearses ----------

    private static string Committed(string file)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Tendero.slnx")))
            folder = folder.Parent;
        return File.ReadAllText(Path.Combine(folder!.FullName, "tools", "SearchEval", "Copilot", file));
    }

    /// <summary>What the script says to a message: its first step whose <c>when</c> the message contains, as the scripted model matches.</summary>
    private static string? Scripted(JsonElement script, string message) =>
        script.EnumerateArray()
            .Where(step => message.Contains(step.GetProperty("when").GetString()!, StringComparison.OrdinalIgnoreCase))
            .Select(step => step.GetProperty("say").GetString())
            .FirstOrDefault();

    /// <summary>
    /// **A language case is rehearsed whole, or it rehearses nothing.** When an
    /// answer comes in another language than the screen's, the turn asks the model
    /// to say it again — with the answer as the message. A script with no step for
    /// that second message answers *I was not scripted for that*, in English: so
    /// the case that expects English passed on a line nobody wrote for it, and its
    /// twin, which expects Spanish, failed. Every language case therefore has, in
    /// the script, the answer in the wrong language and the step that says it
    /// again in the right one.
    ///
    /// **And the first answer is one the turn can tell is in the wrong language.**
    /// The turn's detector (<see cref="WrittenLanguage"/>) counts function words
    /// and is not the suite's: *There are no returns opened today* holds none of
    /// its words, so the turn let it stand and nothing was said again.
    /// </summary>
    [Fact]
    public void The_dry_run_says_each_language_case_again_in_the_language_it_expects()
    {
        var cases = JsonSerializer.Deserialize<CopilotCases>(Committed("cases.json"), CopilotScoring.Json)!
            .Cases.Where(expected => expected.AnswerLanguage is not null).ToList();
        var script = Json(Committed("dry-run.json"));

        Assert.NotEmpty(cases);
        Assert.All(cases, expected =>
        {
            var first = Scripted(script, expected.Question);
            Assert.NotNull(first);
            // The rehearsal is of the repair: the first answer is in the message's language, not the
            // screen's — plainly, by the turn's own detector, or the turn asks for nothing.
            Assert.NotEqual(expected.AnswerLanguage, CopilotScoring.LanguageOf(first));
            Assert.True(
                WrittenLanguage.Of(first) is { } written && written != expected.AnswerLanguage,
                $"{expected.Id}: the turn cannot tell \"{first}\" is not in {expected.AnswerLanguage}, and would let it stand.");

            var again = Scripted(script, first);
            Assert.True(again is not null, $"{expected.Id}: the script has no step for saying \"{first}\" again.");
            Assert.Equal(expected.AnswerLanguage, CopilotScoring.LanguageOf(again));
            // And the turn takes what was said again only when it is plainly in the screen's language:
            // *No hay devoluciones abiertas hoy* holds none of its words, and the first answer stood.
            Assert.True(
                WrittenLanguage.Of(again) == expected.AnswerLanguage,
                $"{expected.Id}: the turn cannot tell \"{again}\" is in {expected.AnswerLanguage}, and would keep the first answer.");
        });
    }

    [Fact]
    public void A_proposal_nobody_expected_fails_the_case()
    {
        var verdict = CopilotScoring.Judge(
            Case([]), [], [new StoredProposal("WithdrawProduct", "CJ0BBKTB5A", Json("{}"))], Codes, Measure);

        Assert.False(verdict.ExactProposals);
        Assert.Contains("proposed besides: WithdrawProduct CJ0BBKTB5A", verdict.Problems);
    }

    [Fact]
    public void A_proposal_for_another_product_is_not_the_one_expected()
    {
        var verdict = CopilotScoring.Judge(
            Case([], [new ExpectedProposal("WithdrawProduct", "Pista padel shoes", null)]),
            [], [new StoredProposal("WithdrawProduct", "0B6K18J7PB", Json("{}"))], Codes, Measure);

        Assert.False(verdict.ExactProposals);
    }

    [Fact]
    public void A_turn_that_failed_does_not_pass_even_when_nothing_was_expected()
    {
        var verdict = CopilotScoring.Judge(Case([]), [], [], Codes, Measure with { Error = "503" });

        Assert.False(verdict.Passed);
    }

    /// <summary>A case naming a product the laboratory does not have is the suite's mistake, said before a model is blamed.</summary>
    [Fact]
    public void A_case_naming_a_product_the_laboratory_lacks_is_refused()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => CopilotScoring.Judge(
            Case([], [new ExpectedProposal("WithdrawProduct", "Moka pot", null)]), [], [], Codes, Measure));

        Assert.Contains("Moka pot", failure.Message);
    }

    /// <summary>
    /// A misspelt tool or command in the cases is the suite's mistake: named before
    /// any model is asked, or it reads as the model never calling it (review of S-6).
    /// </summary>
    [Fact]
    public void A_tool_or_command_the_shop_does_not_register_is_named_before_any_model_is_asked()
    {
        var cases = new CopilotCases(1,
        [
            Case([new ExpectedTool("find_products", null), new ExpectedTool("find_prodcts", null)],
                [new ExpectedProposal("SetProductTranslation", "Moka pot", null), new ExpectedProposal("SetProductTranslaton", "Moka pot", null)]),
        ]);

        var unknown = CopilotScoring.Unregistered(cases, ["find_products"], ["SetProductTranslation"]);

        Assert.Equal(["tool find_prodcts", "command SetProductTranslaton"], unknown);
    }

    [Fact]
    public void The_score_is_each_shares_the_latencies_by_nearest_rank_and_the_largest_call()
    {
        var pass = new CaseVerdict("a", true, true, true, [], [], Measure with { Seconds = 10, LargestCall = 3000, MicroEuros = 2000 });
        var fail = new CaseVerdict("b", true, false, true, [], [], Measure with { Seconds = 30, LargestCall = 5200, MicroEuros = 0 });

        var score = CopilotScoring.Summarise([pass, fail]);

        Assert.Equal((2, 1.0, 0.5, 1.0, 0.5), (score.Cases, score.RightTools, score.ValidArguments, score.ExactProposals, score.Passed));
        Assert.Equal((10.0, 30.0, 5200L, 0.001), (score.LatencyP50Seconds, score.LatencyP95Seconds, score.LargestCall, score.EurosPerTurn));
    }
}
