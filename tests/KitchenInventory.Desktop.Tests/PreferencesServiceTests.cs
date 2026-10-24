using System;
using System.IO;
using KitchenInventory.Desktop.Services;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

public class PreferencesServiceTests
{
    private static string PreferencesPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "InventoryApp",
        "preferences.json");

    [Fact]
    public void Set_Then_Get_RoundTripsTypedValues()
    {
        var svc = new PreferencesService();

        svc.Set("test.int", 42);
        svc.Set("test.decimal", 12.5m);
        svc.Set("test.string", "hello");
        svc.Set("test.bool", true);

        Assert.Equal(42, svc.Get<int>("test.int"));
        Assert.Equal(12.5m, svc.Get<decimal>("test.decimal"));
        Assert.Equal("hello", svc.Get<string>("test.string"));
        Assert.True(svc.Get<bool>("test.bool"));
    }

    [Fact]
    public void Get_MissingKey_ReturnsProvidedDefault()
    {
        var svc = new PreferencesService();

        Assert.Equal(-1, svc.Get<int>("definitely.missing.key", -1));
        Assert.Equal("fallback", svc.Get<string>("definitely.missing.key", "fallback"));
    }

    [Fact]
    public void Get_KeyOverwritesPreviousValue()
    {
        var svc = new PreferencesService();

        svc.Set("overwrite.me", 1);
        svc.Set("overwrite.me", 2);

        Assert.Equal(2, svc.Get<int>("overwrite.me"));
    }

    [Fact]
    public void Save_PersistsAcrossInstances_AndRestoresOriginalFile()
    {
        var existed = File.Exists(PreferencesPath);
        var original = existed ? File.ReadAllBytes(PreferencesPath) : null;
        try
        {
            var svc = new PreferencesService();
            svc.Set("roundtrip.key", "persisted-value");
            svc.Set("roundtrip.num", 7);
            svc.Save();

            Assert.True(File.Exists(PreferencesPath));

            var reloaded = new PreferencesService();
            Assert.Equal("persisted-value", reloaded.Get<string>("roundtrip.key"));
            Assert.Equal(7, reloaded.Get<int>("roundtrip.num"));
        }
        finally
        {
            if (original != null)
            {
                File.WriteAllBytes(PreferencesPath, original);
            }
            else if (File.Exists(PreferencesPath))
            {
                File.Delete(PreferencesPath);
            }

            var tmp = PreferencesPath + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}
