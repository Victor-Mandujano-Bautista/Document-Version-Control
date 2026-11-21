using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocControl.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBackupsAndSystemSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfiguracionesSistema",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Clave = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Valor = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ActualizadoPor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FechaActualizacion = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracionesSistema", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RegistrosRespaldo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NombreArchivo = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    RutaArchivo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TamanoBytes = table.Column<long>(type: "bigint", nullable: false),
                    TotalCarpetas = table.Column<int>(type: "int", nullable: false),
                    TotalDocumentos = table.Column<int>(type: "int", nullable: false),
                    TotalVersiones = table.Column<int>(type: "int", nullable: false),
                    TotalArchivosFisicos = table.Column<int>(type: "int", nullable: false),
                    HashIntegridad = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreadoPor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaUltimaRestauracion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DetalleError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegistrosRespaldo", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "ConfiguracionesSistema",
                columns: new[] { "Id", "ActualizadoPor", "Clave", "FechaActualizacion", "Valor" },
                values: new object[] { 1, "Sistema", "PapeleraDiasRetencion", new DateTime(2026, 11, 1, 12, 0, 0, 0, DateTimeKind.Utc), "30" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfiguracionesSistema_Clave",
                table: "ConfiguracionesSistema",
                column: "Clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RegistrosRespaldo_FechaCreacion",
                table: "RegistrosRespaldo",
                column: "FechaCreacion");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfiguracionesSistema");

            migrationBuilder.DropTable(
                name: "RegistrosRespaldo");
        }
    }
}
