using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AngryFoot.ApiService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBulletRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BulletRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BulletId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", nullable: false),
                    RevisedText = table.Column<string>(type: "TEXT", nullable: false),
                    SourceText = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Rationale = table.Column<string>(type: "TEXT", nullable: true),
                    IsAiGenerated = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BulletRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BulletRevisions_Bullets_BulletId",
                        column: x => x.BulletId,
                        principalTable: "Bullets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BulletRevisions_BulletId_Mode_Version",
                table: "BulletRevisions",
                columns: new[] { "BulletId", "Mode", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BulletRevisions");
        }
    }
}
