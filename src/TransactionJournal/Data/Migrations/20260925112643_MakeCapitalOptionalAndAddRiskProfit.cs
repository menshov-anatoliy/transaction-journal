using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransactionJournal.Data.Migrations
{
    /// <inheritdoc />
    public partial class MakeCapitalOptionalAndAddRiskProfit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "AllocatedCapitalUsdt",
                table: "Constructions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "TEXT");

            migrationBuilder.AddColumn<int>(
                name: "ProfitUnit",
                table: "Constructions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ProfitValue",
                table: "Constructions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RiskUnit",
                table: "Constructions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RiskValue",
                table: "Constructions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProfitUnit",
                table: "Constructions");

            migrationBuilder.DropColumn(
                name: "ProfitValue",
                table: "Constructions");

            migrationBuilder.DropColumn(
                name: "RiskUnit",
                table: "Constructions");

            migrationBuilder.DropColumn(
                name: "RiskValue",
                table: "Constructions");

            migrationBuilder.AlterColumn<decimal>(
                name: "AllocatedCapitalUsdt",
                table: "Constructions",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
