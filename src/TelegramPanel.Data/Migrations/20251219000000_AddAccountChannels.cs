using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramPanel.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20251219000000_AddAccountChannels")]
    public partial class AddAccountChannels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 允许 Channels.CreatorAccountId 为空：用于“仅管理员（非本系统创建）”频道
            migrationBuilder.DropForeignKey(
                name: "FK_Channels_Accounts_CreatorAccountId",
                table: "Channels");

            migrationBuilder.AlterColumn<int>(
                name: "CreatorAccountId",
                table: "Channels",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddForeignKey(
                name: "FK_Channels_Accounts_CreatorAccountId",
                table: "Channels",
                column: "CreatorAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.CreateTable(
                name: "AccountChannels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AccountId = table.Column<int>(type: "INTEGER", nullable: false),
                    ChannelId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsCreator = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsAdmin = table.Column<bool>(type: "INTEGER", nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountChannels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountChannels_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccountChannels_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountChannels_ChannelId",
                table: "AccountChannels",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountChannels_AccountId_ChannelId",
                table: "AccountChannels",
                columns: new[] { "AccountId", "ChannelId" },
                unique: true);

            // 为已有“系统创建频道”回填关联表，保证按账号筛选能立即生效
            migrationBuilder.Sql(@"
INSERT OR IGNORE INTO AccountChannels (AccountId, ChannelId, IsCreator, IsAdmin, SyncedAt)
SELECT CreatorAccountId, Id, 1, 1, SyncedAt
FROM Channels
WHERE CreatorAccountId IS NOT NULL;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountChannels");

            migrationBuilder.DropForeignKey(
                name: "FK_Channels_Accounts_CreatorAccountId",
                table: "Channels");

            migrationBuilder.AlterColumn<int>(
                name: "CreatorAccountId",
                table: "Channels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Channels_Accounts_CreatorAccountId",
                table: "Channels",
                column: "CreatorAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}

