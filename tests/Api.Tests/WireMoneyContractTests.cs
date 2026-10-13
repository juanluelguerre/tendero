using System.Text.Json;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// **No amount of money is a number with a fraction** (phase 13b, the wire's
/// spec, W-1) — held on the committed document, which is the contract the front
/// ends' types and an agent's reading both come from.
///
/// The rule is written the strict way round: every fractional number in the
/// contract is either named in <see cref="NotMoney"/>, with what it is, or it is
/// money that has not been moved yet and is named in <see cref="StillToMove"/>.
/// A new <c>decimal Total</c> on a view is in neither, and fails here the day it
/// is written.
///
/// **<see cref="StillToMove"/> is a ratchet**: it lists the debt the audit
/// found (`docs/analysis/agent-facing-api-conventions.md`), a name that has been
/// moved must leave it — the test fails on one that is listed and no longer
/// there — and the phase is over when it is empty.
/// </summary>
public sealed class WireMoneyContractTests
{
    /// <summary>Fractions that are not amounts of money, each with what it is.</summary>
    private static readonly Dictionary<string, string> NotMoney = new(StringComparer.Ordinal)
    {
        ["cancellationRate"] = "a share of orders",
        ["cancellationTarget"] = "a share of orders",
        ["confidence"] = "a model's confidence, 0 to 1",
        ["conversion"] = "a share of carts",
        ["coverageDays"] = "days of stock",
        ["days"] = "days of stock",
        ["defectRate"] = "a share of orders",
        ["defectTarget"] = "a share of orders",
        ["elapsedSeconds"] = "a duration",
        ["hybridNdcgAt10"] = "a search metric",
        ["hybridRecallAt50"] = "a search metric",
        ["lateShipmentRate"] = "a share of orders",
        ["lateShipmentTarget"] = "a share of orders",
        ["lexicalNdcgAt10"] = "a search metric",
        ["lexicalRecallAt50"] = "a search metric",
        ["meanMilliseconds"] = "a duration",
        ["medianDays"] = "days of stock",
        ["ndcgAt10"] = "a search metric",
        ["number"] = "a cell of a table or a specification: its column or its unit says what it is",
        ["perDay"] = "units sold a day",
        ["percent"] = "an offer's percentage off",
        ["rate"] = "a tax's percentage, or a share of stock sold",
        ["recallAt50"] = "a search metric",
        ["returnRate"] = "a share of units",
        ["score"] = "a search's relevance score",
        ["similarity"] = "a cosine similarity",
        ["support"] = "how well evidence supports a claim, 0 to 1",
        ["thresholdNdcgAt10"] = "a search metric's floor",
        ["thresholdRecallAt50"] = "a search metric's floor",
        ["tookMs"] = "a duration",
        ["unitsPerOrder"] = "an average of units",
    };

    /// <summary>
    /// Money that still travels as a number: the audit's list, as `Schema.property`.
    /// It only ever gets shorter — and on 2026-10-13 it reached nothing, from the
    /// sixty-two the audit found. It stays, empty: a name written here again is a
    /// debt taken on in the open, and the test above fails on one taken on quietly.
    /// </summary>
    private static readonly string[] StillToMove =
    [
    ];

    private static List<string> FractionalNumbers()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(OpenApiDocumentTests.CommittedDocumentPath()));
        List<string> found = [];

        foreach (var schema in document.RootElement.GetProperty("components").GetProperty("schemas").EnumerateObject())
        {
            if (!schema.Value.TryGetProperty("properties", out var properties))
                continue;

            foreach (var property in properties.EnumerateObject())
            {
                if (IsFractional(property.Value))
                    found.Add($"{schema.Name}.{property.Name}");
            }
        }

        return found;
    }

    /// <summary>A number that may have a fraction: <c>number</c>, alone or beside <c>null</c>. An <c>integer</c> is not one.</summary>
    private static bool IsFractional(JsonElement schema) =>
        schema.TryGetProperty("type", out var type) && type.ValueKind switch
        {
            JsonValueKind.String => type.GetString() == "number",
            JsonValueKind.Array => type.EnumerateArray().Any(member => member.GetString() == "number"),
            _ => false,
        };

    [Fact]
    public void Every_fraction_in_the_contract_is_named_as_what_it_is_or_as_money_still_to_move()
    {
        var unexplained = FractionalNumbers()
            .Where(field => !NotMoney.ContainsKey(field[(field.IndexOf('.', StringComparison.Ordinal) + 1)..]))
            .Except(StillToMove)
            .ToList();

        Assert.True(unexplained.Count == 0,
            $"""
             {string.Join(", ", unexplained)}

             A number with a fraction in the contract. If it is an amount of money, the
             view carries a Money and the wire writes it as minor units with its
             currency. If it is something else, name it in NotMoney with what it is.
             """);
    }

    [Fact]
    public void Money_that_was_moved_has_left_the_list_of_what_is_still_to_move()
    {
        var moved = StillToMove.Except(FractionalNumbers()).ToList();

        Assert.True(moved.Count == 0,
            $"{string.Join(", ", moved)}: no longer a number in the contract. Take it off StillToMove — the list only gets shorter.");
    }

    /// <summary>The shape itself, as the document declares it: what the front ends' types and an agent read.</summary>
    [Fact]
    public void Money_is_declared_as_a_whole_number_of_minor_units_and_a_currency_code()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(OpenApiDocumentTests.CommittedDocumentPath()));
        var money = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("Money");

        Assert.Equal("object", money.GetProperty("type").GetString());
        Assert.Equal(("integer", "int64"),
            (money.GetProperty("properties").GetProperty("amount").GetProperty("type").GetString(),
                money.GetProperty("properties").GetProperty("amount").GetProperty("format").GetString()));
        // The range a browser reads exactly, declared: what tells a client's
        // generator that a plain number holds it.
        Assert.Equal((-9_007_199_254_740_991, 9_007_199_254_740_991),
            (money.GetProperty("properties").GetProperty("amount").GetProperty("minimum").GetInt64(),
                money.GetProperty("properties").GetProperty("amount").GetProperty("maximum").GetInt64()));
        Assert.Equal(("string", "^[A-Z]{3}$"),
            (money.GetProperty("properties").GetProperty("currency").GetProperty("type").GetString(),
                money.GetProperty("properties").GetProperty("currency").GetProperty("pattern").GetString()));
        Assert.Equal(["amount", "currency"], money.GetProperty("required").EnumerateArray().Select(member => member.GetString()).Order());
    }
}
