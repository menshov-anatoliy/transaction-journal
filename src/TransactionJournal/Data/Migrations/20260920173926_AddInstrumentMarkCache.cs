using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransactionJournal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInstrumentMarkCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InstrumentMarks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Symbol = table.Column<string>(type: "TEXT", nullable: false),
                    MarkPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstrumentMarks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InstrumentMarks_Symbol",
                table: "InstrumentMarks",
                column: "Symbol",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InstrumentMarks");
        }
    }
}
