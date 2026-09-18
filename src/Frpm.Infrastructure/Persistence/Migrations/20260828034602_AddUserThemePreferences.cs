using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Frpm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserThemePreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ThemeMode",
                table: "AspNetUsers",
                type: "TEXT",
                maxLength: 10,
                nullable: false,
                defaultValue: "auto");

            migrationBuilder.AddColumn<string>(
                name: "ThemePrimaryColor",
                table: "AspNetUsers",
                type: "TEXT",
                maxLength: 7,
                nullable: false,
                defaultValue: "#5468ff");

            migrationBuilder.AddColumn<string>(
                name: "ThemeSecondaryColor",
                table: "AspNetUsers",
                type: "TEXT",
                maxLength: 7,
                nullable: false,
                defaultValue: "#52606d");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ThemeMode",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ThemePrimaryColor",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ThemeSecondaryColor",
                table: "AspNetUsers");
        }
    }
}
