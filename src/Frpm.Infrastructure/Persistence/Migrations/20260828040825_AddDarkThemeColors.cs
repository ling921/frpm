using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Frpm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDarkThemeColors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ThemeDarkPrimaryColor",
                table: "AspNetUsers",
                type: "TEXT",
                maxLength: 7,
                nullable: false,
                defaultValue: "#8b9cff");

            migrationBuilder.AddColumn<string>(
                name: "ThemeDarkSecondaryColor",
                table: "AspNetUsers",
                type: "TEXT",
                maxLength: 7,
                nullable: false,
                defaultValue: "#a8b3cf");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ThemeDarkPrimaryColor",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ThemeDarkSecondaryColor",
                table: "AspNetUsers");
        }
    }
}
