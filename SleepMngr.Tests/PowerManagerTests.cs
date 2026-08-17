using SleepMngr;

namespace SleepMngr.Tests;

public class PowerManagerTests
{
    [Fact]
    public void IsPreventingSleep_ReturnsBool()
    {
        var result = PowerManager.IsPreventingSleep();
        Assert.IsType<bool>(result);
    }

    [Fact]
    public void PreventSleep_AllowSleep_NoException()
    {
        PowerManager.PreventSleep();
        Assert.True(PowerManager.IsPreventingSleep());

        PowerManager.AllowSleep();
        Assert.False(PowerManager.IsPreventingSleep());
    }

    [Fact]
    public void PreventSleep_Idempotent()
    {
        PowerManager.PreventSleep();
        PowerManager.PreventSleep(); // second call should not crash
        Assert.True(PowerManager.IsPreventingSleep());
        PowerManager.AllowSleep();
    }

    [Fact]
    public void AllowSleep_Idempotent()
    {
        PowerManager.AllowSleep();
        PowerManager.AllowSleep(); // second call should not crash
        Assert.False(PowerManager.IsPreventingSleep());
    }

    [Fact]
    public void GetLogFile_ReturnsValidPath()
    {
        var logFile = PowerManager.GetLogFile();
        Assert.NotNull(logFile);
        Assert.Contains("SleepMngr", logFile);
        Assert.Contains("sleep_log.txt", logFile);
    }
}
