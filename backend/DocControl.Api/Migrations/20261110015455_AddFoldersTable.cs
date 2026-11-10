using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DocControl.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFoldersTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Carpetas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreadoPor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Color = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Carpetas", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Carpetas",
                columns: new[] { "Id", "Color", "CreadoPor", "FechaCreacion", "Nombre" },
                values: new object[,]
                {
                    { 1, "Rojo", "Elena - Administrador", new DateTime(2026, 7, 4, 12, 0, 0, 0, DateTimeKind.Utc), "Recursos Humanos" },
                    { 2, "Verde", "Elena - Administrador", new DateTime(2026, 9, 2, 12, 0, 0, 0, DateTimeKind.Utc), "Finanzas" },
                    { 3, "Azul", "Elena - Administrador", new DateTime(2026, 10, 17, 12, 0, 0, 0, DateTimeKind.Utc), "Dirección General" },
                    { 4, "Morado", "Roberto - Revisor", new DateTime(2026, 4, 15, 12, 0, 0, 0, DateTimeKind.Utc), "Legal" },
                    { 5, "Cian", "Roberto - Revisor", new DateTime(2026, 9, 17, 12, 0, 0, 0, DateTimeKind.Utc), "Tecnología" },
                    { 6, "Amarillo", "Roberto - Revisor", new DateTime(2026, 10, 25, 12, 0, 0, 0, DateTimeKind.Utc), "Calidad" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Carpetas_Nombre",
                table: "Carpetas",
                column: "Nombre",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Carpetas");
        }
    }
}
