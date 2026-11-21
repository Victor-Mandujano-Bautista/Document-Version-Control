using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocControl.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFolderPermissionsAndContentIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TextoIndexado",
                table: "Versiones",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TextoIndexado",
                table: "Documentos",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PermisosCarpeta",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CarpetaId = table.Column<int>(type: "int", nullable: false),
                    Rol = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    NivelAcceso = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ActualizadoPor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FechaActualizacion = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermisosCarpeta", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PermisosCarpeta_Carpetas_CarpetaId",
                        column: x => x.CarpetaId,
                        principalTable: "Carpetas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 1,
                column: "TextoIndexado",
                value: null);

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 2,
                column: "TextoIndexado",
                value: null);

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 3,
                column: "TextoIndexado",
                value: null);

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 4,
                column: "TextoIndexado",
                value: null);

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 5,
                column: "TextoIndexado",
                value: null);

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 6,
                column: "TextoIndexado",
                value: null);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 1,
                column: "TextoIndexado",
                value: null);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 2,
                column: "TextoIndexado",
                value: null);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 3,
                column: "TextoIndexado",
                value: null);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 4,
                column: "TextoIndexado",
                value: null);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 5,
                column: "TextoIndexado",
                value: null);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 6,
                column: "TextoIndexado",
                value: null);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 7,
                column: "TextoIndexado",
                value: null);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 8,
                column: "TextoIndexado",
                value: null);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 9,
                column: "TextoIndexado",
                value: null);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 10,
                column: "TextoIndexado",
                value: null);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 11,
                column: "TextoIndexado",
                value: null);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 12,
                column: "TextoIndexado",
                value: null);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 13,
                column: "TextoIndexado",
                value: null);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 14,
                column: "TextoIndexado",
                value: null);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 15,
                column: "TextoIndexado",
                value: null);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 16,
                column: "TextoIndexado",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_PermisosCarpeta_CarpetaId_Rol",
                table: "PermisosCarpeta",
                columns: new[] { "CarpetaId", "Rol" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PermisosCarpeta");

            migrationBuilder.DropColumn(
                name: "TextoIndexado",
                table: "Versiones");

            migrationBuilder.DropColumn(
                name: "TextoIndexado",
                table: "Documentos");
        }
    }
}
