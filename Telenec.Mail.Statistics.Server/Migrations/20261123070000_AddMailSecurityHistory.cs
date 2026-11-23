using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Telenec.Mail.Statistics.Server.Data;

#nullable disable

namespace Telenec.Mail.Statistics.Server.Migrations;

/// <summary>
/// Sprint 43.4b: nur additive Tabellen. Die vorhandene
/// Installationsstatistik wird weder geaendert noch geloescht.
/// Nicht automatisch produktiv ausrollen, bevor die Tests gruen sind.
/// </summary>
[DbContext(typeof(StatisticsDbContext))]
[Migration("20261123070000_AddMailSecurityHistory")]
public partial class AddMailSecurityHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MailSecurityCheckpointStates",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false),
                CheckpointJson = table.Column<string>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MailSecurityCheckpointStates", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "MailSecurityIncidents",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Account = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
                FirstObservedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                LastObservedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                ClosedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                NewPassed = table.Column<long>(type: "INTEGER", nullable: false),
                NewRejected = table.Column<long>(type: "INTEGER", nullable: false),
                MaxRiskLevel = table.Column<int>(type: "INTEGER", nullable: false),
                SendingLimitZero = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MailSecurityIncidents", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MailSecurityIncidents_OpenAccount",
            table: "MailSecurityIncidents",
            column: "Account",
            unique: true,
            filter: "\"ClosedAtUtc\" IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_MailSecurityIncidents_ClosedAtUtc",
            table: "MailSecurityIncidents",
            column: "ClosedAtUtc");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MailSecurityIncidents");
        migrationBuilder.DropTable(name: "MailSecurityCheckpointStates");
    }
}
