using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocControl.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCrudAndReviewerRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ComentarioRevision",
                table: "Documentos",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EstadoRevision",
                table: "Documentos",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Aprobado");

            migrationBuilder.AddColumn<string>(
                name: "RevisadoPor",
                table: "Documentos",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "ComentarioRevision", "RevisadoPor" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "ComentarioRevision", "RevisadoPor" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "ComentarioRevision", "RevisadoPor" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "ComentarioRevision", "RevisadoPor" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "ComentarioRevision", "RevisadoPor" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Documentos",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "ComentarioRevision", "RevisadoPor" },
                values: new object[] { null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ComentarioRevision",
                table: "Documentos");

            migrationBuilder.DropColumn(
                name: "EstadoRevision",
                table: "Documentos");

            migrationBuilder.DropColumn(
                name: "RevisadoPor",
                table: "Documentos");
        }
    }
}
