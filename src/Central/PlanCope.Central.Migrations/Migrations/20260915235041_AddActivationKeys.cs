using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanCope.Central.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddActivationKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActivationKeyId",
                schema: "sync",
                table: "registered_nodes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AppVersion",
                schema: "sync",
                table: "registered_nodes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Cue",
                schema: "sync",
                table: "registered_nodes",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EnrolledAt",
                schema: "sync",
                table: "registered_nodes",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<JsonDocument>(
                name: "FingerprintComponents",
                schema: "sync",
                table: "registered_nodes",
                type: "jsonb",
                nullable: false);

            migrationBuilder.AddColumn<string>(
                name: "FingerprintHash",
                schema: "sync",
                table: "registered_nodes",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RevokedAt",
                schema: "sync",
                table: "registered_nodes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "activation_keys",
                schema: "sync",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    KeyHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    KeyPrefix = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    IssuedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MaxActivations = table.Column<int>(type: "integer", nullable: false),
                    ActivationCount = table.Column<int>(type: "integer", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedReason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ScopeCue = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Note = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_activation_keys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "node_credentials",
                schema: "sync",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NodeId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RefreshTokenHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RotatedFrom = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_node_credentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_node_credentials_node_credentials_RotatedFrom",
                        column: x => x.RotatedFrom,
                        principalSchema: "sync",
                        principalTable: "node_credentials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_registered_nodes_Cue",
                schema: "sync",
                table: "registered_nodes",
                column: "Cue");

            migrationBuilder.CreateIndex(
                name: "IX_activation_keys_KeyHash",
                schema: "sync",
                table: "activation_keys",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_activation_keys_KeyPrefix",
                schema: "sync",
                table: "activation_keys",
                column: "KeyPrefix");

            migrationBuilder.CreateIndex(
                name: "IX_activation_keys_ScopeCue",
                schema: "sync",
                table: "activation_keys",
                column: "ScopeCue");

            migrationBuilder.CreateIndex(
                name: "IX_node_credentials_NodeId",
                schema: "sync",
                table: "node_credentials",
                column: "NodeId");

            migrationBuilder.CreateIndex(
                name: "IX_node_credentials_RefreshTokenHash",
                schema: "sync",
                table: "node_credentials",
                column: "RefreshTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_node_credentials_RotatedFrom",
                schema: "sync",
                table: "node_credentials",
                column: "RotatedFrom");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activation_keys",
                schema: "sync");

            migrationBuilder.DropTable(
                name: "node_credentials",
                schema: "sync");

            migrationBuilder.DropIndex(
                name: "IX_registered_nodes_Cue",
                schema: "sync",
                table: "registered_nodes");

            migrationBuilder.DropColumn(
                name: "ActivationKeyId",
                schema: "sync",
                table: "registered_nodes");

            migrationBuilder.DropColumn(
                name: "AppVersion",
                schema: "sync",
                table: "registered_nodes");

            migrationBuilder.DropColumn(
                name: "Cue",
                schema: "sync",
                table: "registered_nodes");

            migrationBuilder.DropColumn(
                name: "EnrolledAt",
                schema: "sync",
                table: "registered_nodes");

            migrationBuilder.DropColumn(
                name: "FingerprintComponents",
                schema: "sync",
                table: "registered_nodes");

            migrationBuilder.DropColumn(
                name: "FingerprintHash",
                schema: "sync",
                table: "registered_nodes");

            migrationBuilder.DropColumn(
                name: "RevokedAt",
                schema: "sync",
                table: "registered_nodes");
        }
    }
}
