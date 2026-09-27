using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransactionJournal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConstructionNameIsManual : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NameIsManual",
                table: "Constructions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NameIsManual",
                table: "Constructions");
        }
    }
}
