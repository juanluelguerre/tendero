using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <summary>
/// A variant stops pointing at one picture; a picture says what it shows
/// (`docs/analysis/catalogue-model.md`, proposal 1).
///
/// The column cannot simply go: what it said is turned first into the tag that
/// replaces it. A picture a variant named as its own now says it shows that
/// variant's colour — <c>"shows": "{"COLOR": "BLACK"}"</c> inside the gallery's
/// jsonb, as <c>Jsonb.ShownConverter</c> reads it. Three cases tag nothing, on
/// purpose: a variant with no colour (its picture is every variant's already), a
/// variant no longer sold (a tag for a colour nobody sells shows on nothing, and
/// the aggregate refuses one), and a picture variants of two colours both named
/// (it cannot be both). A picture that already says something keeps it.
/// </summary>
public partial class _20261008092743_VariantImageBecomesWhatAPictureShows : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            update catalog.products as product
            set images = (
                select jsonb_agg(
                    case
                        when owner.colour is not null and coalesce(gallery.picture ->> 'shows', '') = ''
                            then gallery.picture || jsonb_build_object('shows', jsonb_build_object('COLOR', owner.colour)::text)
                        else gallery.picture
                    end
                    order by gallery.place)
                from jsonb_array_elements(product.images) with ordinality as gallery(picture, place)
                left join lateral (
                    select min(variant.axis_values ->> 'COLOR') as colour
                    from catalog.variants as variant
                    where variant.product_id = product.id
                      and variant.image_id = gallery.picture ->> 'id'
                      and variant.status <> 'Discontinued'
                      and variant.axis_values ->> 'COLOR' is not null
                    having count(distinct variant.axis_values ->> 'COLOR') = 1
                ) as owner on true)
            where jsonb_typeof(product.images) = 'array'
              and jsonb_array_length(product.images) > 0
              and exists (
                  select 1 from catalog.variants as variant
                  where variant.product_id = product.id and variant.image_id is not null);
            """);

        migrationBuilder.DropColumn(
            name: "image_id",
            schema: "catalog",
            table: "variants");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "image_id",
            schema: "catalog",
            table: "variants",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        // Back to one picture a variant: the first that says it shows its colour.
        migrationBuilder.Sql(
            """
            update catalog.variants as variant
            set image_id = (
                select gallery.picture ->> 'id'
                from catalog.products as product,
                     jsonb_array_elements(product.images) with ordinality as gallery(picture, place)
                where product.id = variant.product_id
                  and coalesce(gallery.picture ->> 'shows', '') <> ''
                  and (gallery.picture ->> 'shows')::jsonb ->> 'COLOR' = variant.axis_values ->> 'COLOR'
                order by gallery.place
                limit 1)
            where variant.axis_values ->> 'COLOR' is not null;
            """);
    }
}
