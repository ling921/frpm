using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Frpm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCliPackageSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "CliPackages",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "CliPackages");
        }
    }
}
