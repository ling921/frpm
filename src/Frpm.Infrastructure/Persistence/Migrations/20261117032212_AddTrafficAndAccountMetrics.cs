using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Frpm.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261117032212_AddTrafficAndAccountMetrics : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "TodayTrafficInBytes",
            table: "Tunnels",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "TodayTrafficOutBytes",
            table: "Tunnels",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "TotalTrafficBytes",
            table: "Tunnels",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "BandwidthLimitBytes",
            table: "ProviderAccounts",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "TrafficLimitBytes",
            table: "ProviderAccounts",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "TrafficUsedBytes",
            table: "ProviderAccounts",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "UserRole",
            table: "ProviderAccounts",
            type: "TEXT",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "TodayTrafficInBytes",
            table: "Tunnels");

        migrationBuilder.DropColumn(
            name: "TodayTrafficOutBytes",
            table: "Tunnels");

        migrationBuilder.DropColumn(
            name: "TotalTrafficBytes",
            table: "Tunnels");

        migrationBuilder.DropColumn(
            name: "BandwidthLimitBytes",
            table: "ProviderAccounts");

        migrationBuilder.DropColumn(
            name: "TrafficLimitBytes",
            table: "ProviderAccounts");

        migrationBuilder.DropColumn(
            name: "TrafficUsedBytes",
            table: "ProviderAccounts");

        migrationBuilder.DropColumn(
            name: "UserRole",
            table: "ProviderAccounts");
    }
}
