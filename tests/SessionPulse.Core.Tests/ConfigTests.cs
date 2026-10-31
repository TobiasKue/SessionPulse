using SessionPulse.Core.Config;

namespace SessionPulse.Core.Tests;

public class ConfigTests
{
    [Fact]
    public void EmptyTextGivesDefaults() => Assert.Equal(SessionPulseConfig.Default, SessionPulseConfig.Parse(string.Empty));

    [Fact]
    public void ParsesAllKeys()
    {
        var config = SessionPulseConfig.Parse("# comment\r\nLABELS=0\r\nLABEL_LEN=40\nSTALE_MINUTES=5\nCLEANUP_HOURS=48\nVERBOSE=1\nAPPROVALS=1\nAPPROVAL_TIMEOUT=60\nHAPTICS=0\nSTATUSLINE_CHAIN=npx x\n");

        Assert.False(config.Labels);
        Assert.Equal(40, config.LabelLength);
        Assert.Equal(5, config.StaleMinutes);
        Assert.Equal(48, config.CleanupHours);
        Assert.True(config.Verbose);
        Assert.True(config.Approvals);
        Assert.Equal(60, config.ApprovalTimeout);
        Assert.False(config.Haptics);
    }

    [Theory]
    [InlineData("LABELS=yes")]
    [InlineData("LABEL_LEN=0")]
    [InlineData("LABEL_LEN=201")]
    [InlineData("LABEL_LEN=-5")]
    [InlineData("LABEL_LEN= 10")]
    [InlineData("labels=0")]
    [InlineData("#LABELS=0")]
    [InlineData("UNKNOWN=1")]
    [InlineData("=1")]
    [InlineData("APPROVAL_TIMEOUT=4")]
    [InlineData("APPROVAL_TIMEOUT=281")]
    public void IgnoresInvalidLines(string line) => Assert.Equal(SessionPulseConfig.Default, SessionPulseConfig.Parse(line));

    [Fact]
    public void LoadMissingFileGivesDefaults() =>
        Assert.Equal(SessionPulseConfig.Default, SessionPulseConfig.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
}
