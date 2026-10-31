using SessionPulse.Core.Dictation;

namespace SessionPulse.Core.Tests;

public class DictationConfigTests
{
    [Fact]
    public void ParsesValidConfig()
    {
        var config = DictationConfig.Parse("""{"chatId":"0a1b2c3d-4e5f","startButtons":"Dictate|Diktieren","stopButtons":" Stop "}""")!;

        Assert.Equal("0a1b2c3d-4e5f", config.ChatId);
        Assert.Equal("Dictate|Diktieren", config.StartButtons);
        Assert.Equal("Stop", config.StopButtons);
        Assert.Equal("claude://claude.ai/chat/0a1b2c3d-4e5f", config.ChatLink);
    }

    [Theory]
    [InlineData("""{"chatId":"short","startButtons":"a","stopButtons":"b"}""")]
    [InlineData("""{"chatId":"../../etc/passwd","startButtons":"a","stopButtons":"b"}""")]
    [InlineData("""{"chatId":"0a1b2c3d-4e5f","startButtons":"","stopButtons":"b"}""")]
    [InlineData("""{"chatId":"0a1b2c3d-4e5f","startButtons":"a"}""")]
    [InlineData("not json")]
    [InlineData("[]")]
    public void RejectsInvalidConfig(string json)
    {
        var logged = new List<string>();

        Assert.Null(DictationConfig.Parse(json, logged.Add));
        Assert.Single(logged);
    }

    [Fact]
    public void MissingFileMeansOff() =>
        Assert.Null(DictationConfig.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json")));
}
