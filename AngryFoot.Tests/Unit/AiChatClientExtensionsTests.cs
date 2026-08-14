using AngryFoot.ApiService.Ai;
using AngryFoot.Tests.Fakes;
using AwesomeAssertions;
using Microsoft.Extensions.AI;

namespace AngryFoot.Tests.Unit;

public class AiChatClientExtensionsTests
{
    private sealed record Payload(string Name, IReadOnlyList<string> Items);

    private static List<ChatOptions?> Recorded(out ScriptedChatClient client, params string[] responses)
    {
        var seen = new List<ChatOptions?>();
        var index = 0;

        client = new ScriptedChatClient((_, options) =>
        {
            seen.Add(options);
            var response = responses[Math.Min(index, responses.Length - 1)];
            index++;

            // Sentinels standing in for a deployment rejecting the schema, and for a cancelled call.
            return response switch
            {
                "throw" => throw new InvalidOperationException("response_format 'json_schema' is not supported"),
                "offline" => throw new HttpRequestException("No connection could be made"),
                "cancel" => throw new OperationCanceledException(),
                _ => response
            };
        });

        return seen;
    }

    [Fact]
    public async Task GetJsonResponseAsync_SendsAJsonSchemaForAnObjectRootedType()
    {
        var seen = Recorded(out var client, """{"name":"a","items":["x"]}""");

        var result = await client.GetJsonResponseAsync<Payload>("system", "user", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Value!.Name.Should().Be("a");
        result.SchemaEnforced.Should().BeTrue();

        seen.Should().ContainSingle().Which!.ResponseFormat.Should().BeOfType<ChatResponseFormatJson>(
            "the model should be constrained to the shape rather than asked nicely for it");
    }

    [Fact]
    public async Task GetJsonResponseAsync_WhenTheDeploymentRejectsTheSchema_RetriesWithoutIt()
    {
        var seen = Recorded(out var client, "throw", """{"name":"b","items":[]}""");

        var result = await client.GetJsonResponseAsync<Payload>("system", "user", CancellationToken.None);

        result.Succeeded.Should().BeTrue("an older service version must not break the feature outright");
        result.Value!.Name.Should().Be("b");
        result.SchemaEnforced.Should().BeFalse("the caller can tell the guarantee was not actually applied");

        seen.Should().HaveCount(2);
        seen[0]!.ResponseFormat.Should().NotBeNull();
        seen[1]?.ResponseFormat.Should().BeNull("the retry drops the response format entirely");
    }

    [Fact]
    public async Task GetJsonResponseAsync_KeepsTheRawTextWhenTheAnswerDoesNotParse()
    {
        Recorded(out var client, "I'd rather explain it in prose.");

        var result = await client.GetJsonResponseAsync<Payload>("system", "user", CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.RawText.Should().Be("I'd rather explain it in prose.", "callers log the raw answer to diagnose it");
    }

    [Fact]
    public async Task GetJsonResponseAsync_StillParsesAChattyAnswer()
    {
        Recorded(out var client, """Sure! {"name":"c","items":["y"]} Hope that helps.""");

        var result = await client.GetJsonResponseAsync<Payload>("system", "user", CancellationToken.None);

        result.Succeeded.Should().BeTrue(
            "a schema constrains a provider that honours it; the tolerant parse covers one that does not");
        result.Value!.Name.Should().Be("c");
    }

    [Fact]
    public async Task GetJsonResponseAsync_DoesNotRetryATransportFailureAsThoughItWereTheSchema()
    {
        var seen = Recorded(out var client, "offline");

        var act = () => client.GetJsonResponseAsync<Payload>("system", "user", CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>(
            "a dead connection is the caller's to handle, not something a second call would fix");
        seen.Should().ContainSingle("retrying would spend another call failing the same way");
    }

    [Fact]
    public async Task GetJsonResponseAsync_PropagatesCancellationRatherThanRetrying()
    {
        var seen = Recorded(out var client, "cancel");

        var act = () => client.GetJsonResponseAsync<Payload>("system", "user", CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
        seen.Should().ContainSingle(
            "a cancelled call must not be retried as though the schema were the problem");
    }
}
