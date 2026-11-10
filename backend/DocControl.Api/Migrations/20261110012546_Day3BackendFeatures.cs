using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocControl.Api.Migrations
{
    /// <inheritdoc />
    public partial class Day3BackendFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MotivoCambio",
                table: "Versiones",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "Eliminado",
                table: "Documentos",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Etiquetas",
                table: "Documentos",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaEliminacion",
                table: "Documentos",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DatosMaquina",
                table: "Bitacora",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Eliminado", "Etiquetas", "FechaEliminacion" },
                values: new object[] { false, "Oficial,Normativo", null });

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Eliminado", "Etiquetas", "FechaEliminacion" },
                values: new object[] { false, "Confidencial,Contabilidad", null });

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "Eliminado", "Etiquetas", "FechaEliminacion" },
                values: new object[] { false, "Urgente,Confidencial", null });

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "Eliminado", "Etiquetas", "FechaEliminacion" },
                values: new object[] { false, "Oficial,Legal", null });

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "Eliminado", "Etiquetas", "FechaEliminacion" },
                values: new object[] { false, "Interno", null });

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "Eliminado", "Etiquetas", "FechaEliminacion" },
                values: new object[] { false, "Urgente,Auditoría", null });

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "MotivoCambio", "TamanoBytes" },
                values: new object[] { "Requerimiento de cumplimiento normativo interno", 245760L });

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "MotivoCambio", "TamanoBytes" },
                values: new object[] { "Observaciones de la revisión de estilo corporativo", 248832L });

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "MotivoCambio", "TamanoBytes" },
                values: new object[] { "Entrada en vigor de la NOM-035-STPS", 312320L });

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "MotivoCambio", "TamanoBytes" },
                values: new object[] { "Hallazgo del simulacro de evacuación", 318464L });

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "MotivoCambio", "TamanoBytes" },
                values: new object[] { "Cierre del ciclo de planeación anual", 1556480L });

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "MotivoCambio", "TamanoBytes" },
                values: new object[] { "Solicitud de TI por renovación de licencias", 1617920L });

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "MotivoCambio", "TamanoBytes" },
                values: new object[] { "Petición del comité de finanzas", 1679360L });

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "MotivoCambio", "TamanoBytes" },
                values: new object[] { "Sesión del consejo directivo", 4300800L });

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 9,
                columns: new[] { "MotivoCambio", "TamanoBytes" },
                values: new object[] { "Alta del nuevo esquema de proveedores", 911360L });

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 10,
                columns: new[] { "MotivoCambio", "TamanoBytes" },
                values: new object[] { "Recomendación de auditoría legal", 942080L });

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 11,
                columns: new[] { "MotivoCambio", "TamanoBytes" },
                values: new object[] { "Reforma a la legislación de adquisiciones", 1126400L });

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 12,
                columns: new[] { "MotivoCambio", "TamanoBytes" },
                values: new object[] { "Ajuste inflacionario anual", 1146880L });

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 13,
                columns: new[] { "MotivoCambio", "TamanoBytes" },
                values: new object[] { "Formalización y firma de las partes", 1280000L });

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 14,
                columns: new[] { "MotivoCambio", "TamanoBytes" },
                values: new object[] { "Estandarización de procesos del área", 532480L });

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 15,
                columns: new[] { "MotivoCambio", "TamanoBytes" },
                values: new object[] { "Plan de continuidad del negocio (DRP)", 593920L });

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 16,
                columns: new[] { "MotivoCambio", "TamanoBytes" },
                values: new object[] { "Programa anual de auditorías ISO 9001", 2150400L });

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_Eliminado",
                table: "Documentos",
                column: "Eliminado");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documentos_Eliminado",
                table: "Documentos");

            migrationBuilder.DropColumn(
                name: "MotivoCambio",
                table: "Versiones");

            migrationBuilder.DropColumn(
                name: "Eliminado",
                table: "Documentos");

            migrationBuilder.DropColumn(
                name: "Etiquetas",
                table: "Documentos");

            migrationBuilder.DropColumn(
                name: "FechaEliminacion",
                table: "Documentos");

            migrationBuilder.DropColumn(
                name: "DatosMaquina",
                table: "Bitacora");

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 1,
                column: "TamanoBytes",
                value: 245000L);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 2,
                column: "TamanoBytes",
                value: 248000L);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 3,
                column: "TamanoBytes",
                value: 312000L);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 4,
                column: "TamanoBytes",
                value: 318000L);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 5,
                column: "TamanoBytes",
                value: 1520000L);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 6,
                column: "TamanoBytes",
                value: 1580000L);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 7,
                column: "TamanoBytes",
                value: 1640000L);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 8,
                column: "TamanoBytes",
                value: 4200000L);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 9,
                column: "TamanoBytes",
                value: 890000L);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 10,
                column: "TamanoBytes",
                value: 920000L);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 11,
                column: "TamanoBytes",
                value: 1100000L);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 12,
                column: "TamanoBytes",
                value: 1120000L);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 13,
                column: "TamanoBytes",
                value: 1250000L);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 14,
                column: "TamanoBytes",
                value: 520000L);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 15,
                column: "TamanoBytes",
                value: 580000L);

            migrationBuilder.UpdateData(
                table: "Versiones",
                keyColumn: "Id",
                keyValue: 16,
                column: "TamanoBytes",
                value: 2100000L);
        }
    }
}
