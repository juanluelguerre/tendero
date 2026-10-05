using ElGuerre.Tendero.Assist.Steward;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Assist.Tests;

/// <summary>
/// The rows a model wrote out again are taken from its answer when the panel
/// already draws them (P12-10): a small model copies them whatever the prompt says.
/// </summary>
public sealed class CopiedRowsTests
{
    private static readonly ResultTable Gaps = ResultTable.Of("catalogueGaps",
        [new("code", CellKind.Text), new("name", CellKind.Text)],
        [["MUG-1", "Taza de gres"], ["LAMP-2", "Flexo articulado"], ["BAG-3", "Mochila urbana"], ["PAN-4", "Sartén de hierro"], ["CUP-5", "Vaso térmico"]]);

    [Fact]
    public void A_numbered_list_of_the_tables_rows_goes_and_the_sentences_stay()
    {
        const string answer = """
            Hay 5 productos sin texto alternativo. Aquí están:

            1. Taza de gres
            2. Flexo articulado
            3. Mochila urbana
            4. Sartén de hierro
            5. Vaso térmico

            No puedo describir imágenes.
            """;

        Assert.Equal("Hay 5 productos sin texto alternativo. Aquí están.\n\nNo puedo describir imágenes.", CopiedRows.Without(answer, [Gaps]));
    }

    /// <summary>A line that only announced the rows goes with them; one that states a figure stays, as a sentence.</summary>
    [Fact]
    public void The_line_that_announced_the_rows_does_not_announce_nothing()
    {
        const string answer = """
            There are **33** SKUs to restock:

            Here are a few examples from the list:

            - Taza de gres
            - Flexo articulado
            - Mochila urbana
            - Sartén de hierro

            Shall I narrow it to a warehouse?
            """;

        Assert.Equal("There are **33** SKUs to restock.\n\nShall I narrow it to a warehouse?", CopiedRows.Without(answer, [Gaps]));
    }

    [Fact]
    public void A_markdown_table_of_the_rows_goes_with_its_heading_and_rule()
    {
        const string answer = """
            These need a look:

            | Code | Name |
            |---|---|
            | MUG-1 | Taza de gres |
            | LAMP-2 | Flexo articulado |
            | BAG-3 | Mochila urbana |
            | PAN-4 | Sartén de hierro |

            Tell me which to translate.
            """;

        Assert.Equal("Tell me which to translate.", CopiedRows.Without(answer, [Gaps]));
    }

    /// <summary>The prompt asks it to name a few: that is not a copy.</summary>
    [Fact]
    public void Naming_a_few_rows_is_left_alone()
    {
        const string answer = "Five lack it, among them:\n\n- Taza de gres\n- Flexo articulado\n- Mochila urbana";

        Assert.Equal(answer, CopiedRows.Without(answer, [Gaps]));
    }

    [Fact]
    public void A_list_that_is_not_the_tables_rows_is_left_alone()
    {
        const string answer = "You can:\n\n- translate them\n- withdraw them\n- publish them\n- leave them\n- ask me again";

        Assert.Equal(answer, CopiedRows.Without(answer, [Gaps]));
    }

    [Fact]
    public void What_hangs_from_a_copied_item_goes_with_it()
    {
        const string answer = """
            Found:

            - **MUG-1**
              - Status: Draft
            - **LAMP-2**
              - Status: Draft
            - **BAG-3**
              - Status: Active
            - **PAN-4**
              - Status: Draft

            Done.
            """;

        Assert.Equal("Done.", CopiedRows.Without(answer, [Gaps]));
    }

    /// <summary>A figure or one of the shop's terms identifies no row: only a row's own words do.</summary>
    [Fact]
    public void A_table_of_figures_alone_takes_nothing_out()
    {
        var figures = ResultTable.Of("salesFigures",
            [new("period", CellKind.Term), new("orders", CellKind.Number)], [["current", 12], ["previous", 9]]);
        const string answer = "- current: 12\n- previous: 9\n- current against previous: up\n- current is better";

        Assert.Equal(answer, CopiedRows.Without(answer, [figures]));
    }

    /// <summary>Every way Markdown writes an item counts: "-", "*", "+", "1." and "1)".</summary>
    [Fact]
    public void An_item_is_an_item_whatever_marks_it()
    {
        const string answer = "They are.\n\n- Taza de gres\n* Flexo articulado\n+ Mochila urbana\n1. Sartén de hierro\n2) Vaso térmico";

        Assert.Equal("They are.", CopiedRows.Without(answer, [Gaps]));
    }

    /// <summary>What only looks like an item — a dash with no space, a number with no mark — is a sentence, and stays.</summary>
    [Fact]
    public void A_line_that_only_looks_like_an_item_is_left_alone()
    {
        const string answer = "-Taza de gres\n1 Flexo articulado\n2.Mochila urbana\n-\nSartén de hierro and Vaso térmico";

        Assert.Equal(answer, CopiedRows.Without(answer, [Gaps]));
    }

    /// <summary>A three-letter code names a row; a two-letter cell — a language — names nothing.</summary>
    [Fact]
    public void A_cell_names_a_row_from_three_characters()
    {
        var codes = ResultTable.Of("t", [new("code", CellKind.Text), new("missing", CellKind.Text)],
            [["MUG", "es"], ["CUP", "en"], ["PAN", "es"], ["BAG", "en"]]);

        Assert.Equal("Four.", CopiedRows.Without("Four.\n\n- MUG\n- CUP\n- PAN\n- BAG", [codes]));
        const string languages = "- es\n- en\n- es again\n- en again";
        Assert.Equal(languages, CopiedRows.Without(languages, [codes]));
    }

    /// <summary>Only the table that copies the rows goes: one of the model's own — figures it worked out — stays whole.</summary>
    [Fact]
    public void A_table_that_is_not_a_copy_stays_beside_one_that_is()
    {
        const string answer = """
            | Code | Name |
            |---|---|
            | MUG-1 | Taza de gres |
            | LAMP-2 | Flexo articulado |
            | BAG-3 | Mochila urbana |
            | PAN-4 | Sartén de hierro |

            By kind 2:

            | Kind | Count |
            |---|---|
            | Kitchen | 3 |
            | Bags | 1 |
            """;

        Assert.Equal("By kind 2:\n\n| Kind | Count |\n|---|---|\n| Kitchen | 3 |\n| Bags | 1 |", CopiedRows.Without(answer, [Gaps]));
    }

    [Fact]
    public void With_no_table_drawn_the_answer_is_the_models()
    {
        const string answer = "1. Taza de gres\n2. Flexo articulado\n3. Mochila urbana\n4. Sartén de hierro";

        Assert.Equal(answer, CopiedRows.Without(answer, []));
    }
}
