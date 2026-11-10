using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocControl.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSubfoldersHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Carpetas_Nombre",
                table: "Carpetas");

            migrationBuilder.AddColumn<int>(
                name: "CarpetaId",
                table: "Documentos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CarpetaPadreId",
                table: "Carpetas",
                type: "int",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Carpetas",
                keyColumn: "Id",
                keyValue: 1,
                column: "CarpetaPadreId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Carpetas",
                keyColumn: "Id",
                keyValue: 2,
                column: "CarpetaPadreId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Carpetas",
                keyColumn: "Id",
                keyValue: 3,
                column: "CarpetaPadreId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Carpetas",
                keyColumn: "Id",
                keyValue: 4,
                column: "CarpetaPadreId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Carpetas",
                keyColumn: "Id",
                keyValue: 5,
                column: "CarpetaPadreId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Carpetas",
                keyColumn: "Id",
                keyValue: 6,
                column: "CarpetaPadreId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 1,
                column: "CarpetaId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 2,
                column: "CarpetaId",
                value: 2);

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 3,
                column: "CarpetaId",
                value: 3);

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 4,
                column: "CarpetaId",
                value: 4);

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 5,
                column: "CarpetaId",
                value: 5);

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 6,
                column: "CarpetaId",
                value: 6);

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_CarpetaId",
                table: "Documentos",
                column: "CarpetaId");

            migrationBuilder.CreateIndex(
                name: "IX_Carpetas_Nombre_CarpetaPadreId",
                table: "Carpetas",
                columns: new[] { "Nombre", "CarpetaPadreId" },
                unique: true,
                filter: "[CarpetaPadreId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documentos_CarpetaId",
                table: "Documentos");

            migrationBuilder.DropIndex(
                name: "IX_Carpetas_Nombre_CarpetaPadreId",
                table: "Carpetas");

            migrationBuilder.DropColumn(
                name: "CarpetaId",
                table: "Documentos");

            migrationBuilder.DropColumn(
                name: "CarpetaPadreId",
                table: "Carpetas");

            migrationBuilder.CreateIndex(
                name: "IX_Carpetas_Nombre",
                table: "Carpetas",
                column: "Nombre",
                unique: true);
        }
    }
}
