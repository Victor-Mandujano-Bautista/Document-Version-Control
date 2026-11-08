using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DocControl.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Bitacora",
                columns: table => new
                {
                    Folio = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FechaHora = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Usuario = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Rol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CodigoDocumento = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    NombreDocumento = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Accion = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Observaciones = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bitacora", x => x.Folio);
                });

            migrationBuilder.CreateTable(
                name: "Documentos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Codigo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Departamento = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    VersionActual = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    NumeroVersionActual = table.Column<int>(type: "int", nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaUltimaModificacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documentos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Bloqueos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentoId = table.Column<int>(type: "int", nullable: false),
                    UsuarioBloqueador = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RolBloqueador = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FechaBloqueo = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaExpiracion = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bloqueos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Bloqueos_Documentos_DocumentoId",
                        column: x => x.DocumentoId,
                        principalTable: "Documentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Versiones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentoId = table.Column<int>(type: "int", nullable: false),
                    NumeroVersion = table.Column<int>(type: "int", nullable: false),
                    VersionTexto = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    NombreArchivo = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Autor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Comentario = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    HashSHA256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TamanoBytes = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Versiones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Versiones_Documentos_DocumentoId",
                        column: x => x.DocumentoId,
                        principalTable: "Documentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Documentos",
                columns: new[] { "Id", "Codigo", "Departamento", "Estado", "FechaCreacion", "FechaUltimaModificacion", "Nombre", "NumeroVersionActual", "Tipo", "VersionActual" },
                values: new object[,]
                {
                    { 1, "POL-RH-001", "Recursos Humanos", "Disponible", new DateTime(2026, 7, 4, 12, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 27, 12, 0, 0, 0, DateTimeKind.Utc), "Política de Seguridad e Higiene", 4, "Word", "v2.1" },
                    { 2, "FIN-PRE-002", "Finanzas", "Disponible", new DateTime(2026, 9, 2, 12, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 30, 12, 0, 0, 0, DateTimeKind.Utc), "Presupuesto Anual 2026", 3, "Excel", "v1.2" },
                    { 3, "DIR-EST-003", "Dirección General", "Disponible", new DateTime(2026, 10, 17, 12, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 17, 12, 0, 0, 0, DateTimeKind.Utc), "Plan Estratégico 2026-2028", 1, "PowerPoint", "v1.0" },
                    { 4, "LEG-CON-004", "Legal", "Disponible", new DateTime(2026, 4, 15, 12, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 22, 12, 0, 0, 0, DateTimeKind.Utc), "Contrato Marco Proveedores", 5, "PDF", "v3.0" },
                    { 5, "TI-MAN-005", "Tecnología", "Disponible", new DateTime(2026, 9, 17, 12, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 24, 12, 0, 0, 0, DateTimeKind.Utc), "Manual de Operaciones TI", 2, "Word", "v1.1" },
                    { 6, "CAL-AUD-006", "Calidad", "Disponible", new DateTime(2026, 10, 25, 12, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 25, 12, 0, 0, 0, DateTimeKind.Utc), "Informe de Auditoría Interna Q3", 1, "PDF", "v1.0" }
                });

            migrationBuilder.InsertData(
                table: "Versiones",
                columns: new[] { "Id", "Autor", "Comentario", "DocumentoId", "FechaCreacion", "HashSHA256", "NombreArchivo", "NumeroVersion", "TamanoBytes", "VersionTexto" },
                values: new object[,]
                {
                    { 1, "Lic. María López", "Creación inicial del documento", 1, new DateTime(2026, 7, 4, 12, 0, 0, 0, DateTimeKind.Utc), "5ae72aff9bcb02e91716a8a8e1c5322735af44c3587c6dd3de69fd2f53766fc8", "POL-RH-001_v1.0.docx", 1, 245000L, "v1.0" },
                    { 2, "Lic. María López", "Correcciones de formato y ortografía", 1, new DateTime(2026, 8, 3, 12, 0, 0, 0, DateTimeKind.Utc), "2ac401711f25a79bf803882e90243aa89e99ddf1646746dc791b4a0c3de3aefd", "POL-RH-001_v1.1.docx", 2, 248000L, "v1.1" },
                    { 3, "Carlos Ruiz", "Actualización mayor: nuevas normas NOM-035", 1, new DateTime(2026, 10, 2, 12, 0, 0, 0, DateTimeKind.Utc), "9309be652408157b962022efa590796c02fa32d54c2fa7561fd6a7b748ac54bf", "POL-RH-001_v2.0.docx", 3, 312000L, "v2.0" },
                    { 4, "Carlos Ruiz", "Ajuste en sección 4.2 - Protocolo de emergencia", 1, new DateTime(2026, 10, 27, 12, 0, 0, 0, DateTimeKind.Utc), "038f208458abe9b2ad91e6dce73ca1ebbda28f87ff5c28caa85c0874e8132d69", "POL-RH-001_v2.1.docx", 4, 318000L, "v2.1" },
                    { 5, "C.P. Laura Méndez", "Presupuesto base aprobado por dirección", 2, new DateTime(2026, 9, 2, 12, 0, 0, 0, DateTimeKind.Utc), "d03dfac20c425c09fd08a643dd99d946413df3eca2650657d2abb92fb9d9c422", "FIN-PRE-002_v1.0.xlsx", 1, 1520000L, "v1.0" },
                    { 6, "C.P. Laura Méndez", "Ajuste partida de tecnología Q3", 2, new DateTime(2026, 10, 2, 12, 0, 0, 0, DateTimeKind.Utc), "952f1b7d174c84bd2dd6c1ac77780e589f74d5fd89372b10a9cdf74e8d535e71", "FIN-PRE-002_v1.1.xlsx", 2, 1580000L, "v1.1" },
                    { 7, "Carlos Ruiz", "Incorporación de proyección de gastos operativos", 2, new DateTime(2026, 10, 30, 12, 0, 0, 0, DateTimeKind.Utc), "ede3f366d5dc8fa50022d29de8ac0f74962a6ee76a8bfb81022d26a3f6c90a83", "FIN-PRE-002_v1.2.xlsx", 3, 1640000L, "v1.2" },
                    { 8, "Ing. Roberto Díaz", "Presentación inicial aprobada por comité", 3, new DateTime(2026, 10, 17, 12, 0, 0, 0, DateTimeKind.Utc), "88c2ad88145bb044358f9c3ca9f32d67ae906add8c0e06ee537658960b1cc870", "DIR-EST-003_v1.0.pptx", 1, 4200000L, "v1.0" },
                    { 9, "Lic. Fernanda Torres", "Contrato base redactado por jurídico", 4, new DateTime(2026, 4, 15, 12, 0, 0, 0, DateTimeKind.Utc), "76ec258109223d7dd16914dd3dd31420660a405e0268b6085b10a428a8e5d589", "LEG-CON-004_v1.0.pdf", 1, 890000L, "v1.0" },
                    { 10, "Lic. Fernanda Torres", "Adición de cláusula de confidencialidad", 4, new DateTime(2026, 6, 4, 12, 0, 0, 0, DateTimeKind.Utc), "3020e4b480a8f393f2d7f9562fedbcd7187eeb8f7780a311062528243837cdea", "LEG-CON-004_v1.1.pdf", 2, 920000L, "v1.1" },
                    { 11, "Lic. Fernanda Torres", "Reestructuración completa - nuevo marco legal", 4, new DateTime(2026, 8, 13, 12, 0, 0, 0, DateTimeKind.Utc), "e7af159ac29c6a106e14922605e9368e1698dee1887d9180233e9a95a8700557", "LEG-CON-004_v2.0.pdf", 3, 1100000L, "v2.0" },
                    { 12, "Carlos Ruiz", "Actualización de montos y tarifas", 4, new DateTime(2026, 9, 22, 12, 0, 0, 0, DateTimeKind.Utc), "e33ac1814461a4cd2125fd3612d6763529020078ad621e1746b801a429d401c3", "LEG-CON-004_v2.1.pdf", 4, 1120000L, "v2.1" },
                    { 13, "Ing. Roberto Díaz", "Versión definitiva con firma electrónica", 4, new DateTime(2026, 10, 22, 12, 0, 0, 0, DateTimeKind.Utc), "e7077df6c15ccf391317194509d305c52a584320b817d061cd85d71c10db8e12", "LEG-CON-004_v3.0.pdf", 5, 1250000L, "v3.0" },
                    { 14, "Ing. Roberto Díaz", "Manual inicial de procedimientos de TI", 5, new DateTime(2026, 9, 17, 12, 0, 0, 0, DateTimeKind.Utc), "961c5cc3edf9f1cc47b8929fd9f41a79bba6f789861c51c67db5e558a761f55a", "TI-MAN-005_v1.0.docx", 1, 520000L, "v1.0" },
                    { 15, "Carlos Ruiz", "Agregado procedimiento de respaldos y DR", 5, new DateTime(2026, 10, 24, 12, 0, 0, 0, DateTimeKind.Utc), "36db2ab87be70808f96142c329492ddaf98e9a0ce6446976c3fc3a164a696cb4", "TI-MAN-005_v1.1.docx", 2, 580000L, "v1.1" },
                    { 16, "Ana Gómez", "Informe de hallazgos de auditoría Q3-2026", 6, new DateTime(2026, 10, 25, 12, 0, 0, 0, DateTimeKind.Utc), "15942bc4e7e29be8a854d5960c5a82adc54bf81d022bda8a0bd71d1ecf428518", "CAL-AUD-006_v1.0.pdf", 1, 2100000L, "v1.0" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bitacora_FechaHora",
                table: "Bitacora",
                column: "FechaHora");

            migrationBuilder.CreateIndex(
                name: "IX_Bloqueos_DocumentoId",
                table: "Bloqueos",
                column: "DocumentoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_Codigo",
                table: "Documentos",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Versiones_DocumentoId",
                table: "Versiones",
                column: "DocumentoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Bitacora");

            migrationBuilder.DropTable(
                name: "Bloqueos");

            migrationBuilder.DropTable(
                name: "Versiones");

            migrationBuilder.DropTable(
                name: "Documentos");
        }
    }
}
