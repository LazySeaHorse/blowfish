using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ImageCaptionSearch.Core.Interfaces;
using ImageCaptionSearch.Core.Models;

namespace ImageCaptionSearch.Core.Services;

public class LibraryRegistryService : ILibraryRegistryService
{
    private readonly string _appDataPath;
    private readonly string _registryFilePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private RegistryData? _cache;
    private readonly ILibraryService _libraryService;

    public LibraryRegistryService(ILibraryService libraryService, string? customAppDataPath = null)
    {
        _libraryService = libraryService;
        _appDataPath = customAppDataPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ImageCaptionSearch"
        );
        _registryFilePath = Path.Combine(_appDataPath, "registry.json");
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_cache != null) return;

        await _lock.WaitAsync(ct);
        try
        {
            if (_cache != null) return;

            if (!Directory.Exists(_appDataPath))
            {
                Directory.CreateDirectory(_appDataPath);
            }

            if (!File.Exists(_registryFilePath))
            {
                _cache = new RegistryData();
                await SaveAsync(ct);
            }
            else
            {
                var json = await File.ReadAllTextAsync(_registryFilePath, ct);
                _cache = JsonSerializer.Deserialize<RegistryData>(json) ?? new RegistryData();
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        if (_cache == null) return;
        var json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_registryFilePath, json, ct);
    }

    public async Task<IReadOnlyList<Library>> GetLibrariesAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        return _cache!.Libraries.AsReadOnly();
    }

    public async Task<Library?> GetLibraryByIdAsync(Guid id, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        return _cache!.Libraries.FirstOrDefault(l => l.Id == id);
    }

    public async Task<Library?> GetLastOpenedLibraryAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        if (_cache!.LastOpenedLibraryId == null) return null;
        return _cache.Libraries.FirstOrDefault(l => l.Id == _cache.LastOpenedLibraryId);
    }

    public async Task SetLastOpenedLibraryAsync(Guid id, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await _lock.WaitAsync(ct);
        try
        {
            _cache!.LastOpenedLibraryId = id;
            await SaveAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Library> AddLibraryAsync(string rootPath, string displayName, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var fullPath = Path.GetFullPath(rootPath);
        
        // Validate
        await ValidateLibraryRootAsync(fullPath, ct);

        await _lock.WaitAsync(ct);
        try
        {
            var library = new Library(
                Id: Guid.NewGuid(),
                RootPath: fullPath,
                DisplayName: displayName,
                CreatedUtc: DateTime.UtcNow,
                UpdatedUtc: DateTime.UtcNow
            );

            _cache!.Libraries.Add(library);
            await SaveAsync(ct);

            // Initialize local storage and DBs
            await _libraryService.InitializeLibraryAsync(library, ct);

            return library;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveLibraryAsync(Guid id, bool deleteLocalData = false, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await _lock.WaitAsync(ct);
        try
        {
            var lib = _cache!.Libraries.FirstOrDefault(l => l.Id == id);
            if (lib == null) return;

            _cache.Libraries.Remove(lib);
            if (_cache.LastOpenedLibraryId == id)
            {
                _cache.LastOpenedLibraryId = null;
            }

            if (deleteLocalData)
            {
                var internalPath = Path.Combine(lib.RootPath, ".imagecaptionsearch");
                if (Directory.Exists(internalPath))
                {
                    Directory.Delete(internalPath, true);
                }
            }

            await SaveAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateLibraryAsync(Library library, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await _lock.WaitAsync(ct);
        try
        {
            var index = _cache!.Libraries.FindIndex(l => l.Id == library.Id);
            if (index == -1) return;

            _cache.Libraries[index] = library with { UpdatedUtc = DateTime.UtcNow };
            await SaveAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task ValidateLibraryRootAsync(string rootPath, CancellationToken ct)
    {
        // 1. Check if folder exists
        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"Library root folder not found: {rootPath}");
        }

        // 2. Check for duplicate
        var existing = _cache!.Libraries.FirstOrDefault(l => 
            string.Equals(l.RootPath, rootPath, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            throw new InvalidOperationException("This folder is already a registered library.");
        }

        // 3. Check for nesting
        foreach (var lib in _cache.Libraries)
        {
            if (IsSubfolder(rootPath, lib.RootPath) || IsSubfolder(lib.RootPath, rootPath))
            {
                throw new InvalidOperationException("Nested libraries are not allowed.");
            }
        }
        
        // 4. Check for write access
        try
        {
            var testFile = Path.Combine(rootPath, ".test_" + Guid.NewGuid());
            await File.WriteAllTextAsync(testFile, "test", ct);
            File.Delete(testFile);
        }
        catch (Exception ex)
        {
            throw new UnauthorizedAccessException("Cannot write to the library root folder.", ex);
        }
    }

    private static bool IsSubfolder(string path, string parentPath)
    {
        var di1 = new DirectoryInfo(path);
        var di2 = new DirectoryInfo(parentPath);
        var parent = di1.Parent;
        while (parent != null)
        {
            if (string.Equals(parent.FullName, di2.FullName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            parent = parent.Parent;
        }
        return false;
    }

    private sealed class RegistryData
    {
        public List<Library> Libraries { get; set; } = new();
        public Guid? LastOpenedLibraryId { get; set; }
    }
}
