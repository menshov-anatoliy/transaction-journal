using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransactionJournal.Data.Migrations
{
	/// <inheritdoc />
	public partial class AddCoreDomain : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.CreateTable(
				name: "Constructions",
				columns: table => new
				{
					Id = table.Column<long>(type: "INTEGER", nullable: false)
						.Annotation("Sqlite:Autoincrement", true),
					Name = table.Column<string>(type: "TEXT", nullable: false),
					Status = table.Column<string>(type: "TEXT", nullable: false),
					AllocatedCapitalUsdt = table.Column<decimal>(type: "TEXT", nullable: false),
					Comment = table.Column<string>(type: "TEXT", nullable: true)
				},
				constraints: table =>
				{
					table.PrimaryKey("PK_Constructions", x => x.Id);
				});

			migrationBuilder.CreateTable(
				name: "ManualCloseMarks",
				columns: table => new
				{
					Id = table.Column<long>(type: "INTEGER", nullable: false)
						.Annotation("Sqlite:Autoincrement", true),
					ConstructionId = table.Column<long>(type: "INTEGER", nullable: false),
					Symbol = table.Column<string>(type: "TEXT", nullable: false),
					Price = table.Column<decimal>(type: "TEXT", nullable: true),
					MarkedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
				},
				constraints: table =>
				{
					table.PrimaryKey("PK_ManualCloseMarks", x => x.Id);
					table.ForeignKey(
						name: "FK_ManualCloseMarks_Constructions_ConstructionId",
						column: x => x.ConstructionId,
						principalTable: "Constructions",
						principalColumn: "Id",
						onDelete: ReferentialAction.Cascade);
				});

			migrationBuilder.CreateTable(
				name: "PnLAdjustments",
				columns: table => new
				{
					Id = table.Column<long>(type: "INTEGER", nullable: false)
						.Annotation("Sqlite:Autoincrement", true),
					ConstructionId = table.Column<long>(type: "INTEGER", nullable: false),
					Date = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
					Source = table.Column<string>(type: "TEXT", nullable: false),
					AmountUsdt = table.Column<decimal>(type: "TEXT", nullable: false),
					Comment = table.Column<string>(type: "TEXT", nullable: true)
				},
				constraints: table =>
				{
					table.PrimaryKey("PK_PnLAdjustments", x => x.Id);
					table.ForeignKey(
						name: "FK_PnLAdjustments_Constructions_ConstructionId",
						column: x => x.ConstructionId,
						principalTable: "Constructions",
						principalColumn: "Id",
						onDelete: ReferentialAction.Restrict);
				});

			migrationBuilder.CreateTable(
				name: "PositionComments",
				columns: table => new
				{
					Id = table.Column<long>(type: "INTEGER", nullable: false)
						.Annotation("Sqlite:Autoincrement", true),
					ConstructionId = table.Column<long>(type: "INTEGER", nullable: false),
					Symbol = table.Column<string>(type: "TEXT", nullable: false),
					Text = table.Column<string>(type: "TEXT", nullable: false)
				},
				constraints: table =>
				{
					table.PrimaryKey("PK_PositionComments", x => x.Id);
					table.ForeignKey(
						name: "FK_PositionComments_Constructions_ConstructionId",
						column: x => x.ConstructionId,
						principalTable: "Constructions",
						principalColumn: "Id",
						onDelete: ReferentialAction.Cascade);
				});

			migrationBuilder.CreateTable(
				name: "TradeUserdata",
				columns: table => new
				{
					Id = table.Column<long>(type: "INTEGER", nullable: false)
						.Annotation("Sqlite:Autoincrement", true),
					ExecId = table.Column<string>(type: "TEXT", nullable: false),
					ConstructionId = table.Column<long>(type: "INTEGER", nullable: true),
					Comment = table.Column<string>(type: "TEXT", nullable: true)
				},
				constraints: table =>
				{
					table.PrimaryKey("PK_TradeUserdata", x => x.Id);
					table.ForeignKey(
						name: "FK_TradeUserdata_Constructions_ConstructionId",
						column: x => x.ConstructionId,
						principalTable: "Constructions",
						principalColumn: "Id",
						onDelete: ReferentialAction.Restrict);
				});

			migrationBuilder.CreateIndex(
				name: "IX_ManualCloseMarks_ConstructionId",
				table: "ManualCloseMarks",
				column: "ConstructionId");

			migrationBuilder.CreateIndex(
				name: "IX_PnLAdjustments_ConstructionId",
				table: "PnLAdjustments",
				column: "ConstructionId");

			migrationBuilder.CreateIndex(
				name: "IX_PositionComments_ConstructionId_Symbol",
				table: "PositionComments",
				columns: new[] { "ConstructionId", "Symbol" },
				unique: true);

			migrationBuilder.CreateIndex(
				name: "IX_TradeUserdata_ConstructionId",
				table: "TradeUserdata",
				column: "ConstructionId");

			migrationBuilder.CreateIndex(
				name: "IX_TradeUserdata_ExecId",
				table: "TradeUserdata",
				column: "ExecId",
				unique: true);
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.DropTable(
				name: "ManualCloseMarks");

			migrationBuilder.DropTable(
				name: "PnLAdjustments");

			migrationBuilder.DropTable(
				name: "PositionComments");

			migrationBuilder.DropTable(
				name: "TradeUserdata");

			migrationBuilder.DropTable(
				name: "Constructions");
		}
	}
}
