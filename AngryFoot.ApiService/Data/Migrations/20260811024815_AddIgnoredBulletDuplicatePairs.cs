using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AngryFoot.ApiService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIgnoredBulletDuplicatePairs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IgnoredBulletDuplicatePairs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BulletIdA = table.Column<Guid>(type: "TEXT", nullable: false),
                    BulletIdB = table.Column<Guid>(type: "TEXT", nullable: false),
                    Similarity = table.Column<double>(type: "REAL", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IgnoredBulletDuplicatePairs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IgnoredBulletDuplicatePairs_BulletIdA_BulletIdB",
                table: "IgnoredBulletDuplicatePairs",
                columns: new[] { "BulletIdA", "BulletIdB" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IgnoredBulletDuplicatePairs");
        }
    }
}
