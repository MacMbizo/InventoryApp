using System;
using KitchenInventory.Desktop.Services;
using KitchenInventory.Desktop.ViewModels;
using KitchenInventory.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

public class ItemsViewModelDialogCommandTests
{
    private static ServiceProvider BuildServices(bool includeDialogService)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Minimal EF Core factory (not used by the assertion but required by constructor)
        services.AddDbContextFactory<KitchenInventoryDbContext>(options =>
        {
            options.UseSqlite("Data Source=:memory:");
        });

        // Register concrete desktop services (not invoked in this test)
        services.AddSingleton<IFileSaveService, FileSaveService>();
        services.AddSingleton<IFileOpenService, FileOpenService>();
        services.AddSingleton<ICsvImportService, CsvImportService>();
        services.AddSingleton<IPreferencesService, PreferencesService>();

        if (includeDialogService)
        {
            // Use a lightweight test double to avoid WPF Application.Current usage.
            services.AddSingleton<IDialogService, TestDialogService>();
        }

        services.AddTransient<ItemsViewModel>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private sealed class TestDialogService : IDialogService
    {
        public System.Threading.Tasks.Task<object?> ShowAsync(System.Windows.Window dialog)
        {
            // Never actually shown in this unit test; just a stub implementation
            return System.Threading.Tasks.Task.FromResult<object?>(null);
        }
    }

    [Fact]
    public void When_DialogService_Registered_AddItemCommand_Is_AsyncRelayCommand()
    {
        using var provider = BuildServices(includeDialogService: true);
        var vm = provider.GetRequiredService<ItemsViewModel>();

        Assert.NotNull(vm.AddItemCommand);
        var typeName = vm.AddItemCommand!.GetType().FullName ?? string.Empty;
        Assert.Contains("AsyncRelayCommand", typeName);
        Assert.DoesNotContain("RelayCommand)", typeName);
    }

    [Fact]
    public void When_DialogService_Missing_AddItemCommand_Is_Sync_RelayCommand()
    {
        using var provider = BuildServices(includeDialogService: false);
        var vm = provider.GetRequiredService<ItemsViewModel>();

        Assert.NotNull(vm.AddItemCommand);
        var typeName = vm.AddItemCommand!.GetType().FullName ?? string.Empty;
        Assert.Contains("RelayCommand", typeName);
        Assert.DoesNotContain("AsyncRelayCommand", typeName);
    }
}