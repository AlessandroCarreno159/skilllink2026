using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkillLink_dotnet.Data.Migrations
{
    /// <inheritdoc />
    public partial class ValoracionRespuestas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ParentId",
                table: "Valoraciones",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RespuestaAUsuarioId",
                table: "Valoraciones",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Valoraciones_AutorId",
                table: "Valoraciones",
                column: "AutorId");

            migrationBuilder.AddForeignKey(
                name: "FK_Valoraciones_AspNetUsers_AutorId",
                table: "Valoraciones",
                column: "AutorId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Valoraciones_AspNetUsers_AutorId",
                table: "Valoraciones");

            migrationBuilder.DropIndex(
                name: "IX_Valoraciones_AutorId",
                table: "Valoraciones");

            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "Valoraciones");

            migrationBuilder.DropColumn(
                name: "RespuestaAUsuarioId",
                table: "Valoraciones");
        }
    }
}
