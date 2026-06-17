using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanCope.Central.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "exam");

            migrationBuilder.EnsureSchema(
                name: "core");

            migrationBuilder.EnsureSchema(
                name: "sync");

            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.EnsureSchema(
                name: "publication");

            migrationBuilder.EnsureSchema(
                name: "settings");

            migrationBuilder.CreateTable(
                name: "answer_keys",
                schema: "exam",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamBlockId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CorrectAnswer = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    ScoreValue = table.Column<decimal>(type: "numeric", nullable: true),
                    Metadata = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_answer_keys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "asset_usages",
                schema: "exam",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamBlockId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UsageType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_asset_usages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "assets",
                schema: "exam",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamVersionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    MimeType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Checksum = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    StoragePath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "block_options",
                schema: "exam",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamBlockId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Value = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Label = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    Metadata = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_block_options", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "blocks",
                schema: "exam",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamVersionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    BlockType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Config = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    Validation = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_blocks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "classrooms",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SchoolId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Shift = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Metadata = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_classrooms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cursors",
                schema: "sync",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NodeId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CursorKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CursorValue = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cursors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "delivery_sessions",
                schema: "sync",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RemoteLocalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SchoolId = table.Column<string>(type: "text", nullable: true),
                    ExamVersionId = table.Column<string>(type: "text", nullable: true),
                    ClassroomCode = table.Column<string>(type: "text", nullable: true),
                    CommissionCode = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_sessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "departments",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProvinceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_departments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "exams",
                schema: "exam",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Level = table.Column<string>(type: "text", nullable: true),
                    Area = table.Column<string>(type: "text", nullable: true),
                    Subject = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "inbox",
                schema: "sync",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceNodeId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AggregateType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AggregateId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Payload = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "localities",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DepartmentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PostalCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_localities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "logs",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: true),
                    EntityType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Action = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Payload = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    IpAddress = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "packages",
                schema: "publication",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamVersionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PackageVersion = table.Column<int>(type: "integer", nullable: false),
                    Checksum = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Manifest = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_packages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "provinces",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provinces", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "received_student_attempts",
                schema: "sync",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RemoteLocalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DeliverySessionId = table.Column<string>(type: "text", nullable: true),
                    StudentCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_received_student_attempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "received_submission_answers",
                schema: "sync",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StudentAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BlockId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Answer = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_received_submission_answers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "registered_nodes",
                schema: "sync",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SchoolId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    NodeCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DeviceName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_registered_nodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "schools",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Cue = table.Column<long>(type: "bigint", nullable: false),
                    Annex = table.Column<int>(type: "integer", nullable: true),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    LocalityId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schools", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "settings",
                schema: "settings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Value = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sync_attempts",
                schema: "sync",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NodeId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Direction = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Summary = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    Error = table.Column<JsonDocument>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_attempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "targets",
                schema: "publication",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PublicationPackageId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_targets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                schema: "core",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RoleId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_roles", x => new { x.UserId, x.RoleId });
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    FullName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "versions",
                schema: "exam",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Metadata = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    ReviewedBy = table.Column<string>(type: "text", nullable: true),
                    ApprovedBy = table.Column<string>(type: "text", nullable: true),
                    PublishedBy = table.Column<string>(type: "text", nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_versions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_answer_keys_ExamBlockId",
                schema: "exam",
                table: "answer_keys",
                column: "ExamBlockId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_asset_usages_ExamAssetId",
                schema: "exam",
                table: "asset_usages",
                column: "ExamAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_asset_usages_ExamBlockId",
                schema: "exam",
                table: "asset_usages",
                column: "ExamBlockId");

            migrationBuilder.CreateIndex(
                name: "IX_assets_Checksum",
                schema: "exam",
                table: "assets",
                column: "Checksum");

            migrationBuilder.CreateIndex(
                name: "IX_assets_ExamVersionId",
                schema: "exam",
                table: "assets",
                column: "ExamVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_block_options_ExamBlockId_OrderIndex",
                schema: "exam",
                table: "block_options",
                columns: new[] { "ExamBlockId", "OrderIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_blocks_ExamVersionId_OrderIndex",
                schema: "exam",
                table: "blocks",
                columns: new[] { "ExamVersionId", "OrderIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_classrooms_SchoolId",
                schema: "core",
                table: "classrooms",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_cursors_NodeId_CursorKey",
                schema: "sync",
                table: "cursors",
                columns: new[] { "NodeId", "CursorKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_delivery_sessions_RemoteLocalId",
                schema: "sync",
                table: "delivery_sessions",
                column: "RemoteLocalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_departments_ProvinceId",
                schema: "core",
                table: "departments",
                column: "ProvinceId");

            migrationBuilder.CreateIndex(
                name: "IX_exams_Code",
                schema: "exam",
                table: "exams",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inbox_IdempotencyKey",
                schema: "sync",
                table: "inbox",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_localities_DepartmentId",
                schema: "core",
                table: "localities",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_logs_CreatedAt",
                schema: "audit",
                table: "logs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_packages_ExamVersionId_PackageVersion",
                schema: "publication",
                table: "packages",
                columns: new[] { "ExamVersionId", "PackageVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_received_student_attempts_IdempotencyKey",
                schema: "sync",
                table: "received_student_attempts",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_received_submission_answers_StudentAttemptId",
                schema: "sync",
                table: "received_submission_answers",
                column: "StudentAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_registered_nodes_NodeCode",
                schema: "sync",
                table: "registered_nodes",
                column: "NodeCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_roles_Code",
                schema: "core",
                table: "roles",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_schools_Cue",
                schema: "core",
                table: "schools",
                column: "Cue");

            migrationBuilder.CreateIndex(
                name: "IX_schools_LocalityId",
                schema: "core",
                table: "schools",
                column: "LocalityId");

            migrationBuilder.CreateIndex(
                name: "IX_settings_Key",
                schema: "settings",
                table: "settings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sync_attempts_StartedAt",
                schema: "sync",
                table: "sync_attempts",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_targets_PublicationPackageId_TargetType_TargetId",
                schema: "publication",
                table: "targets",
                columns: new[] { "PublicationPackageId", "TargetType", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_users_Email",
                schema: "core",
                table: "users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_versions_ExamId_VersionNumber",
                schema: "exam",
                table: "versions",
                columns: new[] { "ExamId", "VersionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "answer_keys",
                schema: "exam");

            migrationBuilder.DropTable(
                name: "asset_usages",
                schema: "exam");

            migrationBuilder.DropTable(
                name: "assets",
                schema: "exam");

            migrationBuilder.DropTable(
                name: "block_options",
                schema: "exam");

            migrationBuilder.DropTable(
                name: "blocks",
                schema: "exam");

            migrationBuilder.DropTable(
                name: "classrooms",
                schema: "core");

            migrationBuilder.DropTable(
                name: "cursors",
                schema: "sync");

            migrationBuilder.DropTable(
                name: "delivery_sessions",
                schema: "sync");

            migrationBuilder.DropTable(
                name: "departments",
                schema: "core");

            migrationBuilder.DropTable(
                name: "exams",
                schema: "exam");

            migrationBuilder.DropTable(
                name: "inbox",
                schema: "sync");

            migrationBuilder.DropTable(
                name: "localities",
                schema: "core");

            migrationBuilder.DropTable(
                name: "logs",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "packages",
                schema: "publication");

            migrationBuilder.DropTable(
                name: "provinces",
                schema: "core");

            migrationBuilder.DropTable(
                name: "received_student_attempts",
                schema: "sync");

            migrationBuilder.DropTable(
                name: "received_submission_answers",
                schema: "sync");

            migrationBuilder.DropTable(
                name: "registered_nodes",
                schema: "sync");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "core");

            migrationBuilder.DropTable(
                name: "schools",
                schema: "core");

            migrationBuilder.DropTable(
                name: "settings",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "sync_attempts",
                schema: "sync");

            migrationBuilder.DropTable(
                name: "targets",
                schema: "publication");

            migrationBuilder.DropTable(
                name: "user_roles",
                schema: "core");

            migrationBuilder.DropTable(
                name: "users",
                schema: "core");

            migrationBuilder.DropTable(
                name: "versions",
                schema: "exam");
        }
    }
}
