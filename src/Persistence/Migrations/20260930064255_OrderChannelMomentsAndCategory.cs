using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElGuerre.Tendero.Persistence.Migrations;

/// <inheritdoc />
public partial class _20260930064255_OrderChannelMomentsAndCategory : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "channel",
            schema: "ordering",
            table: "orders",
            type: "character varying(10)",
            maxLength: 10,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "confirmed_at",
            schema: "ordering",
            table: "orders",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "shipped_at",
            schema: "ordering",
            table: "orders",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_orders_channel_created_at",
            schema: "ordering",
            table: "orders",
            columns: new[] { "channel", "created_at" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_orders_channel_created_at",
            schema: "ordering",
            table: "orders");

        migrationBuilder.DropColumn(
            name: "channel",
            schema: "ordering",
            table: "orders");

        migrationBuilder.DropColumn(
            name: "confirmed_at",
            schema: "ordering",
            table: "orders");

        migrationBuilder.DropColumn(
            name: "shipped_at",
            schema: "ordering",
            table: "orders");
    }
}
