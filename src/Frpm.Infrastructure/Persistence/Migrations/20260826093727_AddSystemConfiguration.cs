using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Frpm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SystemConfigurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderSyncIntervalSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    OperatingSystem = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    OperatingSystemDescription = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Architecture = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    FrameworkDescription = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ApplicationVersion = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FirstStartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastStartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemConfigurations", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemConfigurations");
        }
    }
}
