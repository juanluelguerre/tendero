using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20260903162943_AddProductCode : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The slug stops being a lookup key, so the index that made looking it
        // up cheap stops earning its keep (ADR 0026).
        //
        // The migration that created it, two hours earlier, stays in the log. It
        // was applied, and an applied migration is not deleted however wrong the
        // idea behind it turned out to be — the log is append-only precisely so
        // that a database somewhere else can still get from where it is to where
        // the model is. Deleting it would have left every developer's database
        // holding an index no migration mentions and a history row for a file
        // that no longer exists.
        migrationBuilder.DropIndex(
            name: "ix_products_slug",
            schema: "catalog",
            table: "products");

        // NULLABLE first, and with no default.
        //
        // EF generated this column as `nullable: false, defaultValue: ""`, which
        // is the only thing it can do when asked to add a required column to a
        // table that already has rows — and it is exactly wrong here: every
        // existing product would have been given the SAME code, and the unique
        // index two statements below would have rejected the second one. Adding
        // a column, backfilling it and only then tightening it is the three-step
        // any required column needs, and no generator can do it for you because
        // only you know what the values should be.
        migrationBuilder.AddColumn<string>(
            name: "code",
            schema: "catalog",
            table: "products",
            type: "character(10)",
            fixedLength: true,
            maxLength: 10,
            nullable: true);

        // Hex, uppercased, is a strict SUBSET of Crockford's base32 — the digits
        // and A-F are all in the alphabet — so a backfilled code is a valid one
        // and needs no second pass. It carries forty bits where a minted code
        // carries fifty, which is a distinction without a difference for the
        // handful of rows a development database holds.
        migrationBuilder.Sql(
            """
            UPDATE catalog.products
            SET code = upper(substr(replace(gen_random_uuid()::text, '-', ''), 1, 10))
            WHERE code IS NULL;
            """);

        migrationBuilder.AlterColumn<string>(
            name: "code",
            schema: "catalog",
            table: "products",
            type: "character(10)",
            fixedLength: true,
            maxLength: 10,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character(10)",
            oldFixedLength: true,
            oldMaxLength: 10,
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "ux_products_code",
            schema: "catalog",
            table: "products",
            column: "code",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ux_products_code",
            schema: "catalog",
            table: "products");

        migrationBuilder.DropColumn(
            name: "code",
            schema: "catalog",
            table: "products");

        migrationBuilder.CreateIndex(
            name: "ix_products_slug",
            schema: "catalog",
            table: "products",
            column: "slug")
            .Annotation("Npgsql:IndexMethod", "gin");
    }
}
