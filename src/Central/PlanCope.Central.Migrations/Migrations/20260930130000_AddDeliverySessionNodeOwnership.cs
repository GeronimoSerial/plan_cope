using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanCope.Central.Migrations.Migrations;

public partial class AddDeliverySessionNodeOwnership : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_delivery_sessions_RemoteLocalId",
            schema: "sync",
            table: "delivery_sessions");

        migrationBuilder.AddColumn<string>(
            name: "SourceNodeId",
            schema: "sync",
            table: "delivery_sessions",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_delivery_sessions_SourceNodeId_RemoteLocalId",
            schema: "sync",
            table: "delivery_sessions",
            columns: new[] { "SourceNodeId", "RemoteLocalId" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_delivery_sessions_SourceNodeId_RemoteLocalId",
            schema: "sync",
            table: "delivery_sessions");

        migrationBuilder.DropColumn(
            name: "SourceNodeId",
            schema: "sync",
            table: "delivery_sessions");

        migrationBuilder.CreateIndex(
            name: "IX_delivery_sessions_RemoteLocalId",
            schema: "sync",
            table: "delivery_sessions",
            column: "RemoteLocalId",
            unique: true);
    }
}
