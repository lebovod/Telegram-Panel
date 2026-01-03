using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramPanel.Data.Migrations;

/// <inheritdoc />
public partial class AddChannelForwardRules : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ChannelForwardRules",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                BotId = table.Column<int>(type: "INTEGER", nullable: false),
                SourceChannelId = table.Column<long>(type: "INTEGER", nullable: false),
                SourceChannelUsername = table.Column<string>(type: "TEXT", nullable: true),
                SourceChannelTitle = table.Column<string>(type: "TEXT", nullable: true),
                TargetChannelIds = table.Column<string>(type: "TEXT", nullable: false),
                IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                FooterTemplate = table.Column<string>(type: "TEXT", nullable: true),
                DeleteAfterKeywords = table.Column<string>(type: "TEXT", nullable: true),
                DeletePatterns = table.Column<string>(type: "TEXT", nullable: true),
                DeleteLinks = table.Column<bool>(type: "INTEGER", nullable: false),
                DeleteMentions = table.Column<bool>(type: "INTEGER", nullable: false),
                LastProcessedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                LastProcessedMessageId = table.Column<int>(type: "INTEGER", nullable: true),
                ForwardedCount = table.Column<int>(type: "INTEGER", nullable: false),
                SkippedCount = table.Column<int>(type: "INTEGER", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ChannelForwardRules", x => x.Id);
                table.ForeignKey(
                    name: "FK_ChannelForwardRules_Bots_BotId",
                    column: x => x.BotId,
                    principalTable: "Bots",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ChannelForwardRules_BotId",
            table: "ChannelForwardRules",
            column: "BotId");

        migrationBuilder.CreateIndex(
            name: "IX_ChannelForwardRules_SourceChannelId",
            table: "ChannelForwardRules",
            column: "SourceChannelId");

        migrationBuilder.CreateIndex(
            name: "IX_ChannelForwardRules_IsEnabled",
            table: "ChannelForwardRules",
            column: "IsEnabled");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ChannelForwardRules");
    }
}

