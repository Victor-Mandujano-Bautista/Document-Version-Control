using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DocControl.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTagsCommentsAndPreview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Comentarios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentoId = table.Column<int>(type: "int", nullable: false),
                    VersionNumero = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Autor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RolAutor = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Texto = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comentarios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Comentarios_Documentos_DocumentoId",
                        column: x => x.DocumentoId,
                        principalTable: "Documentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Comentarios",
                columns: new[] { "Id", "Autor", "DocumentoId", "FechaCreacion", "RolAutor", "Texto", "VersionNumero" },
                values: new object[,]
                {
                    { 1, "Elena Martínez", 1, new DateTime(2026, 10, 28, 12, 0, 0, 0, DateTimeKind.Utc), "Administrador", "@Carlos revisar el apartado 2 en la próxima actualización para incluir las nuevas rutas de evacuación.", "v2.1" },
                    { 2, "Roberto Díaz", 2, new DateTime(2026, 10, 31, 12, 0, 0, 0, DateTimeKind.Utc), "Revisor", "@Elena las proyecciones de gastos operativos ya cuadran con el cierre contable. Listo para comité.", "v1.2" },
                    { 3, "Carlos Ruiz", 4, new DateTime(2026, 10, 30, 12, 0, 0, 0, DateTimeKind.Utc), "Editor", "@Roberto @Elena confirmo que la cláusula de confidencialidad quedó reflejada en la versión vigente v3.0.", "General" }
                });

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 1,
                column: "Etiquetas",
                value: "Oficial,Final");

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 2,
                column: "Etiquetas",
                value: "Urgente,Contabilidad");

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 4,
                column: "Etiquetas",
                value: "Oficial,Final");

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 5,
                column: "Etiquetas",
                value: "En Proceso,Oficial");

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 6,
                column: "Etiquetas",
                value: "Urgente,Confidencial");

            migrationBuilder.CreateIndex(
                name: "IX_Comentarios_DocumentoId_FechaCreacion",
                table: "Comentarios",
                columns: new[] { "DocumentoId", "FechaCreacion" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Comentarios");

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 1,
                column: "Etiquetas",
                value: "Oficial,Normativo");

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 2,
                column: "Etiquetas",
                value: "Confidencial,Contabilidad");

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 4,
                column: "Etiquetas",
                value: "Oficial,Legal");

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 5,
                column: "Etiquetas",
                value: "Interno");

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 6,
                column: "Etiquetas",
                value: "Urgente,Auditoría");
        }
    }
}
