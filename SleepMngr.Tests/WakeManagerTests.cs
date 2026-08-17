using SleepMngr;

namespace SleepMngr.Tests;

public class WakeManagerTests
{
    [Fact]
    public void IsMouseWakeDisabled_InitiallyFalse()
    {
        // Fresh state: mouse wake should not be disabled
        Assert.False(WakeManager.IsMouseWakeDisabled);
    }

    [Fact]
    public void GetWakeArmedDevices_ReturnsListOfStrings()
    {
        var devices = WakeManager.GetWakeArmedDevices();
        Assert.IsType<List<string>>(devices);
    }

    [Fact]
    public void GetWakeArmedMouseDevices_ReturnsListOfStrings()
    {
        var devices = WakeManager.GetWakeArmedMouseDevices();
        Assert.IsType<List<string>>(devices);
    }

    [Fact]
    public void GetWakeArmedMouseDevices_ReturnsList()
    {
        var devices = WakeManager.GetWakeArmedMouseDevices();
        Assert.NotNull(devices);
        Assert.IsType<List<string>>(devices);
    }

    [Fact]
    public void DisableMouseWake_EnableMouseWake_NoException()
    {
        // Should not throw even if no devices are found
        var result1 = WakeManager.DisableMouseWake();
        var result2 = WakeManager.EnableMouseWake();

        // Both should return bool without exception
        Assert.IsType<bool>(result1);
        Assert.IsType<bool>(result2);
    }

    [Fact]
    public void GetDebugInfo_ReturnsString()
    {
        var info = WakeManager.GetDebugInfo();
        Assert.NotNull(info);
        Assert.IsType<string>(info);
    }
}
