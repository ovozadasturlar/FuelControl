using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelControl.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ZaxiraSozlamalari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ZaxiraSozlamalari",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    TelegramTokenShifr = table.Column<string>(type: "TEXT", nullable: true),
                    TelegramChatId = table.Column<string>(type: "TEXT", nullable: true),
                    TelegramYoqilgan = table.Column<bool>(type: "INTEGER", nullable: false),
                    YuborishVaqti = table.Column<DateTime>(type: "TEXT", nullable: true),
                    YuborishMuvaffaqiyat = table.Column<bool>(type: "INTEGER", nullable: true),
                    YuborishXabari = table.Column<string>(type: "TEXT", nullable: true),
                    YuborishTuri = table.Column<string>(type: "TEXT", nullable: true),
                    OxirgiYuborilganFayl = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZaxiraSozlamalari", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ZaxiraSozlamalari");
        }
    }
}
