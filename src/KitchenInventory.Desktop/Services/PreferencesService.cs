using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace KitchenInventory.Desktop.Services;

public sealed class PreferencesService : IPreferencesService
{
    private readonly string _filePath;
    private readonly Dictionary<string, JsonElement> _map;
    private readonly object _lock = new();

    public PreferencesService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var baseDir = Path.Combine(appData, "InventoryApp");
        Directory.CreateDirectory(baseDir);
        _filePath = Path.Combine(baseDir, "preferences.json");
        _map = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        TryLoad();
    }

    private void TryLoad()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json)) return;
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    _map[prop.Name] = prop.Value.Clone();
                }
            }
        }
        catch
        {
            // ignore corrupt preferences; start fresh
        }
    }

    public T? Get<T>(string key, T? @default = default)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var elem))
            {
                try
                {
                    // Use raw text to round-trip numbers precisely
                    return JsonSerializer.Deserialize<T>(elem.GetRawText());
                }
                catch { return @default; }
            }
            return @default;
        }
    }

    public void Set<T>(string key, T value)
    {
        lock (_lock)
        {
            _map[key] = JsonSerializer.SerializeToElement(value);
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            using var doc = BuildDocument();
            var json = JsonSerializer.Serialize(doc.RootElement, options);
            var tmp = _filePath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(_filePath)) File.Delete(_filePath);
            File.Move(tmp, _filePath);
        }
    }

    private JsonDocument BuildDocument()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var kvp in _map)
            {
                writer.WritePropertyName(kvp.Key);
                kvp.Value.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        stream.Position = 0;
        return JsonDocument.Parse(stream);
    }
}