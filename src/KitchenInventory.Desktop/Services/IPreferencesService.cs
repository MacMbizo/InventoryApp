using System;

namespace KitchenInventory.Desktop.Services;

public interface IPreferencesService
{
    T? Get<T>(string key, T? @default = default);
    void Set<T>(string key, T value);
    void Save();
}