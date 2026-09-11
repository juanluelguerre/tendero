using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20260911104416_ReturnNumberWithinOrder : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Replaced, not dropped for nothing: the unique index below starts with
        // order_id, so it serves every lookup this one did.
        migrationBuilder.DropIndex(
            name: "ix_return_requests_order_id",
            schema: "ordering",
            table: "return_requests");

        // NULLABLE first, and with no default — the three steps AddProductCode
        // explains. EF generated `nullable: false, defaultValue: 0`, which would
        // have given every existing return the SAME number and made the unique
        // index refuse the second return of any order that has two. A
        // development database has exactly that: the double refund of
        // 2026-09-11 left two returns on one order.
        migrationBuilder.AddColumn<int>(
            name: "number",
            schema: "ordering",
            table: "return_requests",
            type: "integer",
            nullable: true);

        // Numbered in the order they were opened, per order — which is what the
        // domain does for every new one (earlier returns + 1).
        migrationBuilder.Sql(
            """
            UPDATE ordering.return_requests AS target
            SET number = numbered.number
            FROM (
                SELECT id, row_number() OVER (PARTITION BY order_id ORDER BY created_at, id) AS number
                FROM ordering.return_requests
            ) AS numbered
            WHERE target.id = numbered.id;
            """);

        migrationBuilder.AlterColumn<int>(
            name: "number",
            schema: "ordering",
            table: "return_requests",
            type: "integer",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "integer",
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "ux_return_requests_order_id_number",
            schema: "ordering",
            table: "return_requests",
            columns: new[] { "order_id", "number" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ux_return_requests_order_id_number",
            schema: "ordering",
            table: "return_requests");

        migrationBuilder.DropColumn(
            name: "number",
            schema: "ordering",
            table: "return_requests");

        migrationBuilder.CreateIndex(
            name: "ix_return_requests_order_id",
            schema: "ordering",
            table: "return_requests",
            column: "order_id");
    }
}
