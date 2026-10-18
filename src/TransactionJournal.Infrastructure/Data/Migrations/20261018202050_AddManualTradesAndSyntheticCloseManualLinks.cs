using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransactionJournal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddManualTradesAndSyntheticCloseManualLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Quantity",
                table: "ManualCloseMarks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ManualTrades",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Symbol = table.Column<string>(type: "TEXT", nullable: false),
                    Side = table.Column<string>(type: "TEXT", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    Price = table.Column<decimal>(type: "TEXT", nullable: false),
                    FeeUsdt = table.Column<decimal>(type: "TEXT", nullable: false),
                    ConstructionId = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManualTrades", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManualTrades_Constructions_ConstructionId",
                        column: x => x.ConstructionId,
                        principalTable: "Constructions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SyntheticCloseManualLinks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConstructionId = table.Column<long>(type: "INTEGER", nullable: false),
                    LinearTradeId = table.Column<string>(type: "TEXT", nullable: false),
                    OptionTradeId = table.Column<string>(type: "TEXT", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyntheticCloseManualLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyntheticCloseManualLinks_Constructions_ConstructionId",
                        column: x => x.ConstructionId,
                        principalTable: "Constructions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManualTrades_ConstructionId",
                table: "ManualTrades",
                column: "ConstructionId");

            migrationBuilder.CreateIndex(
                name: "IX_SyntheticCloseManualLinks_ConstructionId",
                table: "SyntheticCloseManualLinks",
                column: "ConstructionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ManualTrades");

            migrationBuilder.DropTable(
                name: "SyntheticCloseManualLinks");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "ManualCloseMarks");
        }
    }
}
