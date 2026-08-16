using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AngryFoot.ApiService.Data.Migrations;

/// <inheritdoc />
public partial class AddGenerationArtifactExplanation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "GenerationExplanationJson",
            table: "GenerationArtifacts",
            type: "TEXT",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "GenerationExplanationJson",
            table: "GenerationArtifacts");
    }
}
