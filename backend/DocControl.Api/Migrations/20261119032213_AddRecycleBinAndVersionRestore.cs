using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocControl.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRecycleBinAndVersionRestore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EliminadoPor",
                table: "Documentos",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 1,
                column: "EliminadoPor",
                value: null);

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 2,
                column: "EliminadoPor",
                value: null);

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 3,
                column: "EliminadoPor",
                value: null);

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 4,
                column: "EliminadoPor",
                value: null);

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 5,
                column: "EliminadoPor",
                value: null);

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 6,
                column: "EliminadoPor",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EliminadoPor",
                table: "Documentos");
        }
    }
}
