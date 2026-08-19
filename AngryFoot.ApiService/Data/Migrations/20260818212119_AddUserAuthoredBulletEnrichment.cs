using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AngryFoot.ApiService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAuthoredBulletEnrichment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Both columns are read back through a JSON value converter, and "" is not JSON. EF's
            // default for a non-nullable string would make every existing bullet throw on read - the
            // same trap AddBulletQualityAcknowledgements documents.
            const string emptySet = @"{""tags"":[],""skills"":[],""technologies"":[],""jobCategories"":[]}";

            migrationBuilder.AddColumn<string>(
                name: "Suppressed",
                table: "Bullets",
                type: "TEXT",
                nullable: false,
                defaultValue: emptySet);

            migrationBuilder.AddColumn<string>(
                name: "UserAuthored",
                table: "Bullets",
                type: "TEXT",
                nullable: false,
                defaultValue: emptySet);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Suppressed",
                table: "Bullets");

            migrationBuilder.DropColumn(
                name: "UserAuthored",
                table: "Bullets");
        }
    }
}
