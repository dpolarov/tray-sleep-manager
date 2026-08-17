using SleepMngr;

namespace SleepMngr.Tests;

public class WorkModeTests
{
    [Fact]
    public void WorkMode_HasExpectedValues()
    {
        Assert.Equal(3, Enum.GetValues<WorkMode>().Length);
        Assert.Contains(Enum.GetValues<WorkMode>(), (WorkMode x) => x == WorkMode.Auto);
        Assert.Contains(Enum.GetValues<WorkMode>(), (WorkMode x) => x == WorkMode.AlwaysPrevent);
        Assert.Contains(Enum.GetValues<WorkMode>(), (WorkMode x) => x == WorkMode.AlwaysAllow);
    }

    [Fact]
    public void WorkMode_Auto_IsZero()
    {
        Assert.Equal(0, (int)WorkMode.Auto);
    }
}
