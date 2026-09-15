using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <summary>
/// A cart line's product name and variant label become LocalizedText.
///
/// There is no DDL: the lines already live in one jsonb column. What changes is
/// the shape inside it, and the carts already saved have to follow, or the first
/// basket a returning shopper opens throws on read. Each plain string becomes the
/// culture -> text dictionary the converter writes, keyed on the culture the cart
/// itself carries — the only language anybody can know that string was in.
///
/// Only lines that are still strings are touched, so running it twice changes
/// nothing. Orders are untouched on purpose: an order froze ONE language because
/// it is the record of one purchase, and that stays right.
/// </summary>
public partial class _20260915085907_CartLineLocalizedNames : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE ordering.carts AS cart
            SET lines = (
                SELECT jsonb_agg(
                           line.element
                           || jsonb_build_object(
                                  'productName',
                                  jsonb_build_object(cart.culture, line.element ->> 'productName')::text)
                           || CASE
                                  WHEN jsonb_typeof(line.element -> 'variantLabel') = 'string'
                                      THEN jsonb_build_object(
                                               'variantLabel',
                                               jsonb_build_object(cart.culture, line.element ->> 'variantLabel')::text)
                                  ELSE '{}'::jsonb
                              END
                           ORDER BY line.position)
                FROM jsonb_array_elements(cart.lines) WITH ORDINALITY AS line(element, position))
            WHERE jsonb_array_length(cart.lines) > 0
              AND left(cart.lines -> 0 ->> 'productName', 1) <> '{';
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Back to one string: the cart's own culture when the line has it, the
        // first translation otherwise.
        migrationBuilder.Sql(
            """
            UPDATE ordering.carts AS cart
            SET lines = (
                SELECT jsonb_agg(
                           line.element
                           || jsonb_build_object(
                                  'productName',
                                  coalesce(
                                      (line.element ->> 'productName')::jsonb ->> cart.culture,
                                      (SELECT text.value
                                       FROM jsonb_each_text((line.element ->> 'productName')::jsonb) AS text
                                       LIMIT 1)))
                           || CASE
                                  WHEN jsonb_typeof(line.element -> 'variantLabel') = 'string'
                                      THEN jsonb_build_object(
                                               'variantLabel',
                                               coalesce(
                                                   (line.element ->> 'variantLabel')::jsonb ->> cart.culture,
                                                   (SELECT text.value
                                                    FROM jsonb_each_text((line.element ->> 'variantLabel')::jsonb) AS text
                                                    LIMIT 1)))
                                  ELSE '{}'::jsonb
                              END
                           ORDER BY line.position)
                FROM jsonb_array_elements(cart.lines) WITH ORDINALITY AS line(element, position))
            WHERE jsonb_array_length(cart.lines) > 0
              AND left(cart.lines -> 0 ->> 'productName', 1) = '{';
            """);
    }
}
