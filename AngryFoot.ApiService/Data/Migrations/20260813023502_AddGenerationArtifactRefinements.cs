using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AngryFoot.ApiService.Data.Migrations;

/// <inheritdoc />
public partial class AddGenerationArtifactRefinements : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CoverLetterRefinementJson",
            table: "GenerationArtifacts",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ResumeRefinementJson",
            table: "GenerationArtifacts",
            type: "TEXT",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CoverLetterRefinementJson",
            table: "GenerationArtifacts");

        migrationBuilder.DropColumn(
            name: "ResumeRefinementJson",
            table: "GenerationArtifacts");
    }
}
