using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AngryFoot.ApiService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGenerationArtifactEvidenceCoverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EvidenceCoverageJson",
                table: "GenerationArtifacts",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EvidenceCoverageJson",
                table: "GenerationArtifacts");
        }
    }
}
