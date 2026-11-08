using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocControl.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRutaArchivo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RutaArchivo",
                table: "Versiones",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 1,
                column: "RutaArchivo",
                value: null);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 2,
                column: "RutaArchivo",
                value: null);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 3,
                column: "RutaArchivo",
                value: null);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 4,
                column: "RutaArchivo",
                value: null);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 5,
                column: "RutaArchivo",
                value: null);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 6,
                column: "RutaArchivo",
                value: null);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 7,
                column: "RutaArchivo",
                value: null);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 8,
                column: "RutaArchivo",
                value: null);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 9,
                column: "RutaArchivo",
                value: null);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 10,
                column: "RutaArchivo",
                value: null);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 11,
                column: "RutaArchivo",
                value: null);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 12,
                column: "RutaArchivo",
                value: null);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 13,
                column: "RutaArchivo",
                value: null);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 14,
                column: "RutaArchivo",
                value: null);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 15,
                column: "RutaArchivo",
                value: null);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 16,
                column: "RutaArchivo",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RutaArchivo",
                table: "Versiones");
        }
    }
}
