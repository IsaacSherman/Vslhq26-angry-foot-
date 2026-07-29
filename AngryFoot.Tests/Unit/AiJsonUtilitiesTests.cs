using AngryFoot.ApiService.Ai;
using AwesomeAssertions;

namespace AngryFoot.Tests.Unit;

public class AiJsonUtilitiesTests
{
    private sealed record Payload(string Name, int Count);

    [Fact]
    public void TryDeserialize_WithPlainJsonObject_Succeeds()
    {
        var ok = AiJsonUtilities.TryDeserialize<Payload>("""{"name":"a","count":2}""", out var value);

        ok.Should().BeTrue();
        value.Should().Be(new Payload("a", 2));
    }

    [Fact]
    public void TryDeserialize_WithJsonInsideCodeFence_Succeeds()
    {
        var text = """
            ```json
            {"name":"fenced","count":1}
            ```
            """;

        AiJsonUtilities.TryDeserialize<Payload>(text, out var value).Should().BeTrue();
        value!.Name.Should().Be("fenced");
    }

    [Fact]
    public void TryDeserialize_WithJsonSurroundedByProse_ExtractsTheObject()
    {
        var text = """Sure! Here is the result: {"name":"chatty","count":3} Hope that helps!""";

        AiJsonUtilities.TryDeserialize<Payload>(text, out var value).Should().BeTrue();
        value!.Name.Should().Be("chatty");
    }

    [Fact]
    public void TryDeserialize_WithTopLevelArray_Succeeds()
    {
        var text = """[{"name":"one","count":1},{"name":"two","count":2}]""";

        AiJsonUtilities.TryDeserialize<List<Payload>>(text, out var value).Should().BeTrue();
        value.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no json here at all")]
    [InlineData("{ definitely not valid json }")]
    public void TryDeserialize_WithInvalidInput_ReturnsFalse(string? text)
    {
        AiJsonUtilities.TryDeserialize<Payload>(text, out var value).Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void ToJson_UsesCamelCase()
    {
        AiJsonUtilities.ToJson(new Payload("a", 1)).Should().Be("""{"name":"a","count":1}""");
    }
}
