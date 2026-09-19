using HomeApp.Core;

namespace HomeApp.Core.Tests;

public sealed class SampleDataTests
{
    [Fact]
    public void ParseJson_AcceptsSupportedScalarValues()
    {
        const string json = """
            { "items": [
              { "label": "text", "value": "ok" },
              { "label": "number", "value": 42 },
              { "label": "flag", "value": true },
              { "label": "empty", "value": null }
            ] }
            """;

        var items = SampleData.ParseJson(json);

        Assert.Equal(new[] { "ok", "42", "true", "—" }, items.Select(item => item.Value));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{ \"items\": {} }")]
    [InlineData("{ \"items\": [{ \"label\": \"nested\", \"value\": {} }] }")]
    public void ParseJson_RejectsUnsupportedShapes(string json)
    {
        Assert.Throws<FormatException>(() => SampleData.ParseJson(json));
    }
}
