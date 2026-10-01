using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanCope.Central.Migrations.Migrations;

public partial class AddLiveSessionHeartbeats : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "ClosedOrForcedCount", schema: "sync", table: "delivery_sessions", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "InProgressCount", schema: "sync", table: "delivery_sessions", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "JoinedCount", schema: "sync", table: "delivery_sessions", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<DateTimeOffset>(name: "LastActivityAt", schema: "sync", table: "delivery_sessions", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(name: "LastHeartbeatAt", schema: "sync", table: "delivery_sessions", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<string>(name: "LocalAppVersion", schema: "sync", table: "delivery_sessions", type: "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "RosterSectionId", schema: "sync", table: "delivery_sessions", type: "character varying(128)", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<int>(name: "SubmittedCount", schema: "sync", table: "delivery_sessions", type: "integer", nullable: false, defaultValue: 0);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ClosedOrForcedCount", schema: "sync", table: "delivery_sessions");
        migrationBuilder.DropColumn(name: "InProgressCount", schema: "sync", table: "delivery_sessions");
        migrationBuilder.DropColumn(name: "JoinedCount", schema: "sync", table: "delivery_sessions");
        migrationBuilder.DropColumn(name: "LastActivityAt", schema: "sync", table: "delivery_sessions");
        migrationBuilder.DropColumn(name: "LastHeartbeatAt", schema: "sync", table: "delivery_sessions");
        migrationBuilder.DropColumn(name: "LocalAppVersion", schema: "sync", table: "delivery_sessions");
        migrationBuilder.DropColumn(name: "RosterSectionId", schema: "sync", table: "delivery_sessions");
        migrationBuilder.DropColumn(name: "SubmittedCount", schema: "sync", table: "delivery_sessions");
    }
}
