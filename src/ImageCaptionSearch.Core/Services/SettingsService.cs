using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ImageCaptionSearch.Core.Interfaces;
using ImageCaptionSearch.Core.Models;
using Microsoft.Extensions.Logging;

namespace ImageCaptionSearch.Core.Services;

public class SettingsService : ISettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ImageCaptionSearch",
        "settings.json");

    private readonly ILogger<SettingsService> _logger;
    private AppSettings _cache = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public SettingsService(ILogger<SettingsService> logger)
    {
        _logger = logger;
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read settings from {Path}", SettingsPath);
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
            _logger.LogInformation("Settings saved to {Path}", SettingsPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings to {Path}", SettingsPath);
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }
}
