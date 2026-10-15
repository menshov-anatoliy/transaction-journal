using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransactionJournal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSyntheticCloseDisputes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SyntheticCloseDisputes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConstructionId = table.Column<long>(type: "INTEGER", nullable: false),
                    LinearExecId = table.Column<string>(type: "TEXT", nullable: false),
                    CounterOptionExecId = table.Column<string>(type: "TEXT", nullable: false),
                    DisputedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyntheticCloseDisputes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyntheticCloseDisputes_Constructions_ConstructionId",
                        column: x => x.ConstructionId,
                        principalTable: "Constructions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SyntheticCloseDisputes_ConstructionId_LinearExecId_CounterOptionExecId",
                table: "SyntheticCloseDisputes",
                columns: new[] { "ConstructionId", "LinearExecId", "CounterOptionExecId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyntheticCloseDisputes");
        }
    }
}
