using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AngryFoot.ApiService.Data.Migrations;

/// <inheritdoc />
public partial class AddBulletQualityAcknowledgements : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AcknowledgedQualitySignals",
            table: "Bullets",
            type: "TEXT",
            nullable: false,
            // The column is read back through a JSON value converter, and "" is not JSON.
            // EF's default for a non-nullable string would make every existing bullet throw
            // on read.
            defaultValue: "[]");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AcknowledgedQualitySignals",
            table: "Bullets");
    }
}
