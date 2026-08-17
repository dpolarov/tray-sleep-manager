using SleepMngr;
using System.Drawing;

namespace SleepMngr.Tests;

public class IconGeneratorTests
{
    [Fact]
    public void CreateColoredIcon_ReturnsIcon()
    {
        using var icon = IconGenerator.CreateColoredIcon(Color.Red);
        Assert.NotNull(icon);
        Assert.Equal(32, icon.Width);
        Assert.Equal(32, icon.Height);
    }

    [Fact]
    public void CreateBlueIcon_ReturnsIcon()
    {
        using var icon = IconGenerator.CreateBlueIcon();
        Assert.NotNull(icon);
    }

    [Fact]
    public void CreateYellowIcon_ReturnsIcon()
    {
        using var icon = IconGenerator.CreateYellowIcon();
        Assert.NotNull(icon);
    }

    [Fact]
    public void CreateDarkBlueIcon_ReturnsIcon()
    {
        using var icon = IconGenerator.CreateDarkBlueIcon();
        Assert.NotNull(icon);
    }

    [Fact]
    public void CreateDarkYellowIcon_ReturnsIcon()
    {
        using var icon = IconGenerator.CreateDarkYellowIcon();
        Assert.NotNull(icon);
    }

    [Fact]
    public void CreateColoredIcon_TransparentBackground()
    {
        using var icon = IconGenerator.CreateColoredIcon(Color.Blue);
        using var bmp = icon.ToBitmap();
        // Corner pixel should be transparent (A=0)
        Assert.Equal(0, bmp.GetPixel(0, 0).A);
    }
}
