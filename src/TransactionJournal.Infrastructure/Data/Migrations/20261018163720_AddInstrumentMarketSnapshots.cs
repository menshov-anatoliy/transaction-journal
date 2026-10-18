using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransactionJournal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInstrumentMarketSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InstrumentMarketSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Symbol = table.Column<string>(type: "TEXT", nullable: false),
                    MarkPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    UnderlyingPrice = table.Column<decimal>(type: "TEXT", nullable: true),
                    Theta = table.Column<decimal>(type: "TEXT", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstrumentMarketSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InstrumentMarketSnapshots_Symbol_ReceivedAt",
                table: "InstrumentMarketSnapshots",
                columns: new[] { "Symbol", "ReceivedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InstrumentMarketSnapshots");
        }
    }
}
