using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkillLink_dotnet.Data.Migrations
{
    /// <inheritdoc />
    public partial class CalificacionPorPublicacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "AutorId",
                table: "Valoraciones",
                type: "varchar(255)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Valoraciones_PublicacionId",
                table: "Valoraciones",
                column: "PublicacionId");

            migrationBuilder.CreateIndex(
                name: "IX_Valoraciones_PublicacionId_AutorId",
                table: "Valoraciones",
                columns: new[] { "PublicacionId", "AutorId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Valoraciones_PublicacionId",
                table: "Valoraciones");

            migrationBuilder.DropIndex(
                name: "IX_Valoraciones_PublicacionId_AutorId",
                table: "Valoraciones");

            migrationBuilder.AlterColumn<string>(
                name: "AutorId",
                table: "Valoraciones",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(255)")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }
    }
}
