using System;
using System.IO;
using KitchenInventory.Desktop.Services;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

public class PreferencesServiceTests : IDisposable
{
    private readonly string _dataDir;

    public PreferencesServiceTests()
    {
        _dataDir = Path.Combine(UiTestHelpers.GetRepoRoot(), "artifacts", "ui-test-dbs", Guid.NewGuid().ToString("N"), "appdata");
        Directory.CreateDirectory(_dataDir);
    }

    private string PreferencesPath => Path.Combine(_dataDir, "preferences.json");

    private PreferencesService CreateService() => new(_dataDir);

    public void Dispose()
    {
        try { Directory.Delete(_dataDir, recursive: true); } catch { }
    }

    [Fact]
    public void Set_Then_Get_RoundTripsTypedValues()
    {
        var svc = CreateService();

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
        var svc = CreateService();

        Assert.Equal(-1, svc.Get<int>("definitely.missing.key", -1));
        Assert.Equal("fallback", svc.Get<string>("definitely.missing.key", "fallback"));
    }

    [Fact]
    public void Get_KeyOverwritesPreviousValue()
    {
        var svc = CreateService();

        svc.Set("overwrite.me", 1);
        svc.Set("overwrite.me", 2);

        Assert.Equal(2, svc.Get<int>("overwrite.me"));
    }

    [Fact]
    public void Save_PersistsAcrossInstances()
    {
        var svc = CreateService();
        svc.Set("roundtrip.key", "persisted-value");
        svc.Set("roundtrip.num", 7);
        svc.Save();

        Assert.True(File.Exists(PreferencesPath));

        var reloaded = CreateService();
        Assert.Equal("persisted-value", reloaded.Get<string>("roundtrip.key"));
        Assert.Equal(7, reloaded.Get<int>("roundtrip.num"));
    }
}
