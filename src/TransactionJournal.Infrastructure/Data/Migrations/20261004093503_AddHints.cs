using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransactionJournal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Hints",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RuleId = table.Column<string>(type: "TEXT", nullable: false),
                    SubjectKind = table.Column<string>(type: "TEXT", nullable: false),
                    SubjectConstructionId = table.Column<long>(type: "INTEGER", nullable: true),
                    Character = table.Column<string>(type: "TEXT", nullable: false),
                    Clarity = table.Column<string>(type: "TEXT", nullable: false),
                    SourcesJson = table.Column<string>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    FactsJson = table.Column<string>(type: "TEXT", nullable: false),
                    AsOf = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    WindowPeriodKey = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hints", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Hints_RuleId_SubjectKind_SubjectConstructionId_WindowPeriodKey",
                table: "Hints",
                columns: new[] { "RuleId", "SubjectKind", "SubjectConstructionId", "WindowPeriodKey" });

            migrationBuilder.CreateIndex(
                name: "IX_Hints_Status",
                table: "Hints",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Hints");
        }
    }
}
