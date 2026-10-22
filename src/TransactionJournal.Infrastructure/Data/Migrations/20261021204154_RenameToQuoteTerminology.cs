using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransactionJournal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameToQuoteTerminology : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "MarkPrice",
                table: "InstrumentMarketSnapshots",
                newName: "QuotePrice");

            migrationBuilder.RenameColumn(
                name: "MarkPrice",
                table: "InstrumentMarks",
                newName: "QuotePrice");

            migrationBuilder.RenameTable(
                name: "InstrumentMarks",
                newName: "InstrumentQuotes");

            migrationBuilder.DropIndex(
                name: "IX_InstrumentMarks_Symbol",
                table: "InstrumentQuotes");

            migrationBuilder.CreateIndex(
                name: "IX_InstrumentQuotes_Symbol",
                table: "InstrumentQuotes",
                column: "Symbol",
                unique: true);

            migrationBuilder.RenameColumn(
                name: "MarkedAt",
                table: "ManualCloseMarks",
                newName: "AnnotatedAt");

            migrationBuilder.RenameTable(
                name: "ManualCloseMarks",
                newName: "ManualCloseAnnotations");

            migrationBuilder.DropIndex(
                name: "IX_ManualCloseMarks_ConstructionId",
                table: "ManualCloseAnnotations");

            migrationBuilder.CreateIndex(
                name: "IX_ManualCloseAnnotations_ConstructionId",
                table: "ManualCloseAnnotations",
                column: "ConstructionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "QuotePrice",
                table: "InstrumentMarketSnapshots",
                newName: "MarkPrice");

            migrationBuilder.DropIndex(
                name: "IX_InstrumentQuotes_Symbol",
                table: "InstrumentQuotes");

            migrationBuilder.RenameTable(
                name: "InstrumentQuotes",
                newName: "InstrumentMarks");

            migrationBuilder.RenameColumn(
                name: "QuotePrice",
                table: "InstrumentMarks",
                newName: "MarkPrice");

            migrationBuilder.CreateIndex(
                name: "IX_InstrumentMarks_Symbol",
                table: "InstrumentMarks",
                column: "Symbol",
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_ManualCloseAnnotations_ConstructionId",
                table: "ManualCloseAnnotations");

            migrationBuilder.RenameTable(
                name: "ManualCloseAnnotations",
                newName: "ManualCloseMarks");

            migrationBuilder.RenameColumn(
                name: "AnnotatedAt",
                table: "ManualCloseMarks",
                newName: "MarkedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ManualCloseMarks_ConstructionId",
                table: "ManualCloseMarks",
                column: "ConstructionId");
        }
    }
}
