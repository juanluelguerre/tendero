using System.Text;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Assist.Tino;

/// <summary>A fact with the id it has in this turn: <c>f1</c>, <c>f2</c>… in the order read.</summary>
public sealed record NumberedFact(string Id, Fact Fact);

/// <summary>
/// Everything one turn read that an answer may rest on (the Tino spec, *An
/// answer*): the facts, numbered, and the products the reads returned. It is the
/// whole of what Tino may say — an answer names no product that is not here and
/// cites no fact that is not here, and the answer's schema is built from it so
/// that a model held to a grammar cannot even write one.
/// </summary>
public sealed class FactSheet
{
    /// <summary>
    /// The most facts a turn keeps. Each is a line of the composing call and a value
    /// of its schema's enum; past this a turn has read more than an answer of four
    /// products can use. A first estimate, fixed in T-5.
    /// </summary>
    public const int MaxFacts = 120;

    private readonly Lock gate = new();
    private readonly List<NumberedFact> facts = [];
    private readonly List<Subject> products = [];

    public IReadOnlyList<NumberedFact> Facts => this.facts;

    /// <summary>The products the turn's reads returned, in the order first seen.</summary>
    public IReadOnlyList<Subject> Products => this.products;

    /// <summary>The facts that are about the shop and no product.</summary>
    public IEnumerable<NumberedFact> OfTheShop => this.facts.Where(fact => fact.Fact.Subject == Fact.TheShop);

    public NumberedFact? Find(string? id) => this.facts.FirstOrDefault(fact => fact.Id == id);

    public Subject? Product(string? code) =>
        this.products.FirstOrDefault(product => string.Equals(product.Key, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>What a read returned joins the sheet: a product once, a fact once.</summary>
    public void Add(ReadAnswer answer)
    {
        lock (this.gate)
        {
            foreach (var subject in answer.Subjects ?? [])
                Know(subject);

            foreach (var fact in answer.Facts ?? [])
            {
                if (this.facts.Count >= MaxFacts)
                    break;
                if (this.facts.Any(known => known.Fact.Subject == fact.Subject && known.Fact.Text == fact.Text))
                    continue;

                // A fact about a product the read did not name still makes it one the
                // answer may name: by its code, which is all that is known of it.
                if (fact.Subject != Fact.TheShop)
                    Know(new Subject(fact.Subject, fact.Subject));
                this.facts.Add(new NumberedFact($"f{this.facts.Count + 1}", fact));
            }
        }
    }

    private void Know(Subject subject)
    {
        if (Product(subject.Key) is null)
            this.products.Add(subject);
    }

    /// <summary>
    /// The sheet as the composing call reads it: one line a fact — its id, what it is
    /// about, where it comes from, what it says — and the products by code. It is
    /// data, and the instructions say so: a description is other people's text.
    /// </summary>
    public string ToModel()
    {
        var text = new StringBuilder();
        text.AppendLine("PRODUCTS (code = name):");
        foreach (var product in this.products)
            text.Append(product.Key).Append(" = ").AppendLine(OneLine(product.Name));
        if (this.products.Count == 0)
            text.AppendLine("(none)");

        text.AppendLine().AppendLine("FACTS (id | about | source | fact):");
        foreach (var fact in this.facts)
        {
            text.Append(fact.Id).Append(" | ").Append(fact.Fact.Subject).Append(" | ").Append(fact.Fact.Source)
                .Append(" | ").AppendLine(OneLine(fact.Fact.Text));
        }

        if (this.facts.Count == 0)
            text.AppendLine("(none)");
        return text.ToString();
    }

    /// <summary>A fact is one line of the sheet whatever its text holds: a line break
    /// in a description must not start what reads as a new row.</summary>
    private static string OneLine(string text) => string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
}
