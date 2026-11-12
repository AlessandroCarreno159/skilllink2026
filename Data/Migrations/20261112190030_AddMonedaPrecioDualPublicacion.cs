using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkillLink_dotnet.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMonedaPrecioDualPublicacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Moneda",
                table: "Publicaciones",
                type: "varchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "PEN")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "PrecioDual",
                table: "Publicaciones",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Moneda",
                table: "Publicaciones");

            migrationBuilder.DropColumn(
                name: "PrecioDual",
                table: "Publicaciones");
        }
    }
}
