using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanCope.Central.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddActivationKeyHolder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HolderName",
                schema: "sync",
                table: "activation_keys",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HolderName",
                schema: "sync",
                table: "activation_keys");
        }
    }
}
