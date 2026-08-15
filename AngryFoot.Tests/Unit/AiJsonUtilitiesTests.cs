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

    /// <summary>
    /// The shapes behind the "could not be parsed as JSON" warnings, so it stays on the record
    /// which are recoverable and which are genuinely malformed model output.
    /// </summary>
    public class RealWorldFailureShapes
    {
        [Fact]
        public void ProseContainingABracketBeforeTheObject_StillFindsTheObject()
        {
            var text = """Here is the revision [per your critique]: {"name":"ok","count":1}""";

            AiJsonUtilities.TryDeserialize<Payload>(text, out var value).Should().BeTrue(
                "the bracketed prose is not valid JSON, so the object gets its turn");
            value!.Name.Should().Be("ok");
        }

        [Fact]
        public void ProseContainingBracesBeforeAnArray_StillFindsTheArray()
        {
            var text = """Sure {see below}: [{"name":"a","count":1},{"name":"b","count":2}]""";

            AiJsonUtilities.TryDeserialize<List<Payload>>(text, out var value).Should().BeTrue();
            value.Should().HaveCount(2);
        }

        [Fact]
        public void ASingleObjectWrappedInAnArray_DeserializesAsTheObject()
        {
            var text = """[{"name":"wrapped","count":1}]""";

            AiJsonUtilities.TryDeserialize<Payload>(text, out var value).Should().BeTrue(
                "a model that over-wraps its answer should not cost the caller its fallback");
            value!.Name.Should().Be("wrapped");
        }

        [Fact]
        public void TrailingProseContainingABrace_DoesNotSwallowTheObject()
        {
            var text = """{"name":"ok","count":1} Let me know if you want it shorter :}""";

            AiJsonUtilities.TryDeserialize<Payload>(text, out var value).Should().BeTrue(
                "the balanced scan stops at the object's own closing brace");
            value!.Name.Should().Be("ok");
        }

        [Fact]
        public void DelimitersInsideStringValuesDoNotConfuseTheScan()
        {
            var text = """{"name":"cut costs [by 15%] and {overhead}","count":1}""";

            AiJsonUtilities.TryDeserialize<Payload>(text, out var value).Should().BeTrue();
            value!.Name.Should().Be("cut costs [by 15%] and {overhead}");
        }

        [Fact]
        public void EscapedQuotesInsideStringValuesDoNotConfuseTheScan()
        {
            var text = """{"name":"the \"smart\" way","count":1}""";

            AiJsonUtilities.TryDeserialize<Payload>(text, out var value).Should().BeTrue();
            value!.Name.Should().Be("""the "smart" way""");
        }

        private sealed record Revision(string Revised);

        private sealed record BulletItem(Guid BulletId, string Rewritten);

        /// <summary>
        /// A real reviser response captured from the log on 2026-08-13, the day the stage logged
        /// "could not be parsed": the bullet-set JSON arrives as an escaped string inside the
        /// stage's own envelope, which is exactly what the two-layer contract asks for.
        /// <para>
        /// This payload does <em>not</em> reproduce that failure - it parses under both the old
        /// position-guessing extractor and the current one - so the cause of those warnings is
        /// still open. It is kept because the two-layer unwrap is easy to break by accident.
        /// </para>
        /// </summary>
        [Fact]
        public void RealReviserResponse_WithAnEscapedJsonArrayInsideTheEnvelope_ParsesBothLayers()
        {
            var raw = """
                {"revised":"[{\"bulletId\":\"9412946a-1b2e-46dd-a908-f0d2fd572be6\",\"rewritten\":\"Reduced nightly batch runtime from 6 hours to 40 minutes by reworking the query plan.\"},{\"bulletId\":\"f1bdc59c-65f0-4d22-b8c0-d437fc23e33e\",\"rewritten\":\"Led migration of a monolithic application to containerized services on Azure.\"}]"}
                """;

            AiJsonUtilities.TryDeserialize<Revision>(raw, out var envelope).Should().BeTrue(
                "the envelope is valid JSON whose single field happens to contain more JSON");

            AiJsonUtilities.TryDeserialize<List<BulletItem>>(envelope!.Revised, out var bullets).Should().BeTrue(
                "the caller unwraps the inner array as its own parse");
            bullets.Should().HaveCount(2);
            bullets![0].Rewritten.Should().StartWith("Reduced nightly batch runtime");
        }

        [Fact]
        public void UnescapedNewlineInsideAString_IsMalformedModelOutput()
        {
            var text = "{\"name\":\"line one\nline two\",\"count\":1}";

            AiJsonUtilities.TryDeserialize<Payload>(text, out _).Should().BeFalse(
                "a raw newline inside a JSON string is invalid no matter how it is extracted");
        }

        [Fact]
        public void UnescapedQuoteInsideAString_IsMalformedModelOutput()
        {
            var text = """{"name":"the "smart" way","count":1}""";

            AiJsonUtilities.TryDeserialize<Payload>(text, out _).Should().BeFalse();
        }

        [Fact]
        public void CurlyQuotesAndDashesSurviveFine()
        {
            var text = """{"name":"the “smart” way — really","count":1}""";

            AiJsonUtilities.TryDeserialize<Payload>(text, out var value).Should().BeTrue(
                "smart punctuation is ordinary string content, not an escaping problem");
            value!.Name.Should().Contain("smart");
        }

        [Fact]
        public void BracketsInsideTheStringValueAreFine()
        {
            var text = """{"name":"Managed [redacted] systems","count":1}""";

            AiJsonUtilities.TryDeserialize<Payload>(text, out var value).Should().BeTrue(
                "the object opens before the bracket, so extraction picks the object");
            value!.Name.Should().Be("Managed [redacted] systems");
        }
    }

    [Theory]
    [InlineData(null, "(null)")]
    [InlineData("", "(empty)")]
    public void ForLog_DescribesEmptyResponses(string? text, string expected)
    {
        AiJsonUtilities.ForLog(text).Should().Be(expected);
    }

    [Fact]
    public void ForLog_EscapesControlCharactersSoTheyDoNotSplitTheLogLine()
    {
        AiJsonUtilities.ForLog("line one\r\nline two\ttabbed").Should().Be("line one\\r\\nline two\\ttabbed");
    }

    [Fact]
    public void ForLog_TruncatesLongResponsesAndSaysSo()
    {
        var result = AiJsonUtilities.ForLog(new string('x', 40), maxLength: 10);

        result.Should().StartWith(new string('x', 10));
        result.Should().Contain("truncated, 40 chars total");
    }
}
