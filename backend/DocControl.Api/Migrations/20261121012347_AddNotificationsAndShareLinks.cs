using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DocControl.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationsAndShareLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EnlacesCompartidos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Token = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DocumentoId = table.Column<int>(type: "int", nullable: false),
                    VersionId = table.Column<int>(type: "int", nullable: true),
                    CreadoPor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaExpiracion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Revocado = table.Column<bool>(type: "bit", nullable: false),
                    FechaRevocacion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PermiteDescarga = table.Column<bool>(type: "bit", nullable: false),
                    TotalAccesos = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnlacesCompartidos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnlacesCompartidos_Documentos_DocumentoId",
                        column: x => x.DocumentoId,
                        principalTable: "Documentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Notificaciones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Destinatario = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RolDestinatario = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Titulo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Mensaje = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    DocumentoId = table.Column<int>(type: "int", nullable: true),
                    CodigoDocumento = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Leida = table.Column<bool>(type: "bit", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notificaciones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notificaciones_Documentos_DocumentoId",
                        column: x => x.DocumentoId,
                        principalTable: "Documentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SuscripcionesLiberacion",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentoId = table.Column<int>(type: "int", nullable: false),
                    Usuario = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Rol = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FechaSolicitud = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notificado = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SuscripcionesLiberacion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SuscripcionesLiberacion_Documentos_DocumentoId",
                        column: x => x.DocumentoId,
                        principalTable: "Documentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Notificaciones",
                columns: new[] { "Id", "CodigoDocumento", "Destinatario", "DocumentoId", "FechaCreacion", "Leida", "Mensaje", "RolDestinatario", "Tipo", "Titulo" },
                values: new object[,]
                {
                    { 1, "POL-RH-001", "Carlos Ruiz", 1, new DateTime(2026, 10, 28, 12, 0, 0, 0, DateTimeKind.Utc), false, "Elena Martínez te mencionó en 'Política de Seguridad e Higiene': \"@Carlos revisar el apartado 2 en la próxima actualización para incluir las nuevas rutas de evacuación.\"", "Editor", "MENCION", "Te mencionaron en POL-RH-001" },
                    { 2, "LEG-CON-004", "Roberto Díaz", 4, new DateTime(2026, 10, 30, 12, 0, 0, 0, DateTimeKind.Utc), false, "Carlos Ruiz te mencionó en 'Contrato Marco Proveedores': \"@Roberto @Elena confirmo que la cláusula de confidencialidad quedó reflejada en la versión vigente v3.0.\"", "Revisor", "MENCION", "Te mencionaron en LEG-CON-004" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_EnlacesCompartidos_DocumentoId",
                table: "EnlacesCompartidos",
                column: "DocumentoId");

            migrationBuilder.CreateIndex(
                name: "IX_EnlacesCompartidos_Token",
                table: "EnlacesCompartidos",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notificaciones_DocumentoId",
                table: "Notificaciones",
                column: "DocumentoId");

            migrationBuilder.CreateIndex(
                name: "IX_Notificaciones_RolDestinatario_Leida_FechaCreacion",
                table: "Notificaciones",
                columns: new[] { "RolDestinatario", "Leida", "FechaCreacion" });

            migrationBuilder.CreateIndex(
                name: "IX_SuscripcionesLiberacion_DocumentoId_Notificado",
                table: "SuscripcionesLiberacion",
                columns: new[] { "DocumentoId", "Notificado" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EnlacesCompartidos");

            migrationBuilder.DropTable(
                name: "Notificaciones");

            migrationBuilder.DropTable(
                name: "SuscripcionesLiberacion");
        }
    }
}
