using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransactionJournal.Data.Migrations
{
	/// <inheritdoc />
	public partial class InitialRawStorage : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.CreateTable(
				name: "RawDeliveries",
				columns: table => new
				{
					Id = table.Column<long>(type: "INTEGER", nullable: false)
						.Annotation("Sqlite:Autoincrement", true),
					Symbol = table.Column<string>(type: "TEXT", nullable: false),
					DeliveryTimeMs = table.Column<long>(type: "INTEGER", nullable: false),
					Category = table.Column<string>(type: "TEXT", nullable: false),
					PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
					FetchedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
				},
				constraints: table =>
				{
					table.PrimaryKey("PK_RawDeliveries", x => x.Id);
				});

			migrationBuilder.CreateTable(
				name: "RawExecutions",
				columns: table => new
				{
					Id = table.Column<long>(type: "INTEGER", nullable: false)
						.Annotation("Sqlite:Autoincrement", true),
					ExecId = table.Column<string>(type: "TEXT", nullable: false),
					Category = table.Column<string>(type: "TEXT", nullable: false),
					Symbol = table.Column<string>(type: "TEXT", nullable: false),
					ExecTimeMs = table.Column<long>(type: "INTEGER", nullable: false),
					PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
					FetchedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
				},
				constraints: table =>
				{
					table.PrimaryKey("PK_RawExecutions", x => x.Id);
				});

			migrationBuilder.CreateTable(
				name: "RawInstruments",
				columns: table => new
				{
					Id = table.Column<long>(type: "INTEGER", nullable: false)
						.Annotation("Sqlite:Autoincrement", true),
					Symbol = table.Column<string>(type: "TEXT", nullable: false),
					Category = table.Column<string>(type: "TEXT", nullable: false),
					PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
					FetchedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
				},
				constraints: table =>
				{
					table.PrimaryKey("PK_RawInstruments", x => x.Id);
				});

			migrationBuilder.CreateTable(
				name: "SyncRuns",
				columns: table => new
				{
					Id = table.Column<long>(type: "INTEGER", nullable: false)
						.Annotation("Sqlite:Autoincrement", true),
					StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
					FinishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
					Mode = table.Column<string>(type: "TEXT", nullable: false),
					Status = table.Column<string>(type: "TEXT", nullable: false),
					Error = table.Column<string>(type: "TEXT", nullable: true),
					NewExecutions = table.Column<int>(type: "INTEGER", nullable: false),
					NewDeliveries = table.Column<int>(type: "INTEGER", nullable: false),
					NewInstruments = table.Column<int>(type: "INTEGER", nullable: false)
				},
				constraints: table =>
				{
					table.PrimaryKey("PK_SyncRuns", x => x.Id);
				});

			migrationBuilder.CreateTable(
				name: "SyncStates",
				columns: table => new
				{
					Id = table.Column<long>(type: "INTEGER", nullable: false)
						.Annotation("Sqlite:Autoincrement", true),
					Category = table.Column<string>(type: "TEXT", nullable: false),
					ExecWatermarkMs = table.Column<long>(type: "INTEGER", nullable: true),
					DeliveryWatermarkMs = table.Column<long>(type: "INTEGER", nullable: true),
					BackfillBoundaryMs = table.Column<long>(type: "INTEGER", nullable: true),
					LastSuccessAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
				},
				constraints: table =>
				{
					table.PrimaryKey("PK_SyncStates", x => x.Id);
				});

			migrationBuilder.CreateIndex(
				name: "IX_RawDeliveries_Symbol_DeliveryTimeMs",
				table: "RawDeliveries",
				columns: new[] { "Symbol", "DeliveryTimeMs" },
				unique: true);

			migrationBuilder.CreateIndex(
				name: "IX_RawExecutions_ExecId",
				table: "RawExecutions",
				column: "ExecId",
				unique: true);

			migrationBuilder.CreateIndex(
				name: "IX_RawInstruments_Symbol",
				table: "RawInstruments",
				column: "Symbol",
				unique: true);

			migrationBuilder.CreateIndex(
				name: "IX_SyncStates_Category",
				table: "SyncStates",
				column: "Category",
				unique: true);
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.DropTable(
				name: "RawDeliveries");

			migrationBuilder.DropTable(
				name: "RawExecutions");

			migrationBuilder.DropTable(
				name: "RawInstruments");

			migrationBuilder.DropTable(
				name: "SyncRuns");

			migrationBuilder.DropTable(
				name: "SyncStates");
		}
	}
}
