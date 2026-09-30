using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Telenec.Mail.Statistics.Server.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Installations",
                columns: table => new
                {
                    InstallationKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CurrentVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ConsentVersion = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Installations", x => x.InstallationKey);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Installations_CurrentVersion",
                table: "Installations",
                column: "CurrentVersion");

            migrationBuilder.CreateIndex(
                name: "IX_Installations_FirstSeenUtc",
                table: "Installations",
                column: "FirstSeenUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Installations_LastSeenUtc",
                table: "Installations",
                column: "LastSeenUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Installations");
        }
    }
}
