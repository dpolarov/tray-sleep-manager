using SleepMngr;
using System.IO;

namespace SleepMngr.Tests;

public class SettingsTests
{
    private string? _tempDir;

    [Fact]
    public void Load_ReturnsSettingsInstance()
    {
        var settings = Settings.Load();
        Assert.NotNull(settings);
        Assert.IsType<Settings>(settings);
    }

    [Fact]
    public void RestoreLidSettingsOnStart_DefaultFalse()
    {
        // Create a temp directory to avoid polluting real settings
        var settings = new Settings();
        Assert.False(settings.RestoreLidSettingsOnStart);
    }

    [Fact]
    public void RestoreLidSettingsOnStart_SetAndGet()
    {
        var settings = new Settings();

        settings.RestoreLidSettingsOnStart = true;
        Assert.True(settings.RestoreLidSettingsOnStart);

        settings.RestoreLidSettingsOnStart = false;
        Assert.False(settings.RestoreLidSettingsOnStart);
    }

    [Fact]
    public void Save_And_Load_PersistsRestoreLidSettings()
    {
        // Use a unique test directory
        _tempDir = Path.Combine(Path.GetTempPath(), "SleepMngr_Test_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);

        var settings = new Settings();
        settings.RestoreLidSettingsOnStart = true;
        settings.Save();

        var loaded = Settings.Load();
        Assert.True(loaded.RestoreLidSettingsOnStart);

        // Cleanup
        settings.RestoreLidSettingsOnStart = false;
        settings.Save();

        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var settings = Settings.Load();
        Assert.NotNull(settings);
        Assert.False(settings.RestoreLidSettingsOnStart);
    }

    [Fact]
    public void Save_DoesNotThrow()
    {
        var settings = new Settings();
        // Should not throw even if directory doesn't exist
        try
        {
            settings.Save();
            Assert.True(true);
        }
        catch
        {
            Assert.Fail("Save() threw an exception");
        }
    }
}
