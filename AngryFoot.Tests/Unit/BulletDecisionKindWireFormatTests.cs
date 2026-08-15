using System.Text.Json;
using AngryFoot.Contracts;
using AwesomeAssertions;

namespace AngryFoot.Tests.Unit;

/// <summary>
/// Pins the one enum in these contracts that travels as names rather than as a number. Every other
/// enum serializes as an integer, which survives when one value means one thing; a combined flags
/// value would reach the client as <c>5</c>, which no reader can check without this source file.
/// </summary>
public class BulletDecisionKindWireFormatTests
{
    /// <summary>The options ASP.NET Core minimal APIs use for request and response bodies.</summary>
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void ACombinedDecisionSerializesAsNames()
    {
        var json = JsonSerializer.Serialize(
            BulletDecisionKindDto.Selected | BulletDecisionKindDto.Reordered,
            WebOptions);

        json.Should().Be("\"Selected, Reordered\"");
    }

    [Fact]
    public void ASingleDecisionSerializesAsItsName()
    {
        JsonSerializer.Serialize(BulletDecisionKindDto.Omitted, WebOptions).Should().Be("\"Omitted\"");
    }

    [Theory]
    [InlineData(BulletDecisionKindDto.Omitted)]
    [InlineData(BulletDecisionKindDto.Selected)]
    [InlineData(BulletDecisionKindDto.Selected | BulletDecisionKindDto.Revised)]
    [InlineData(BulletDecisionKindDto.Selected | BulletDecisionKindDto.Reordered)]
    [InlineData(BulletDecisionKindDto.Selected | BulletDecisionKindDto.Revised | BulletDecisionKindDto.Reordered)]
    public void EveryCombinationTheServiceProducesRoundTrips(BulletDecisionKindDto kind)
    {
        var json = JsonSerializer.Serialize(kind, WebOptions);

        JsonSerializer.Deserialize<BulletDecisionKindDto>(json, WebOptions).Should().Be(kind);
    }
}
