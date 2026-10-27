using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransactionJournal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHintsTraceId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TraceId",
                table: "Hints",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TraceId",
                table: "Hints");
        }
    }
}
