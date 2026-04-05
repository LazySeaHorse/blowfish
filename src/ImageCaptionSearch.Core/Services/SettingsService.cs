using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ImageCaptionSearch.Core.Interfaces;
using ImageCaptionSearch.Core.Models;

namespace ImageCaptionSearch.Core.Services;

public class SettingsService : ISettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ImageCaptionSearch",
        "settings.json");

    private AppSettings _cache = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public SettingsService()
    {
    }

    public async Task<AppSettings> GetSettingsAsync(CancellationToken ct = default)
    {
        if (File.Exists(SettingsPath))
        {
            await _lock.WaitAsync(ct);
            try
            {
                var json = await File.ReadAllTextAsync(SettingsPath, ct);
                _cache = JsonSerializer.Deserialize<AppSettings>(json) ?? new();
            }
            catch
            {
                _cache = new();
            }
            finally
            {
                _lock.Release();
            }
        }
        return _cache;
    }

    public async Task UpdateSettingsAsync(AppSettings settings, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _cache = settings;
            var dir = Path.GetDirectoryName(SettingsPath);
            if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(SettingsPath, json, ct);
        }
        finally
        {
            _lock.Release();
        }
    }
}
