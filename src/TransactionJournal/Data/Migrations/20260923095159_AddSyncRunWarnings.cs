using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransactionJournal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncRunWarnings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WarningsJson",
                table: "SyncRuns",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WarningsJson",
                table: "SyncRuns");
        }
    }
}
