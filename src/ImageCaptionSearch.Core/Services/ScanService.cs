using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageCaptionSearch.Core.Interfaces;
using ImageCaptionSearch.Core.Models;

namespace ImageCaptionSearch.Core.Services;

public class ScanService : IScanService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".tif", ".tiff"
    };

    public Task<ScanResult> ScanAsync(string rootPath, IReadOnlyList<ImageRecord> existingImages, CancellationToken ct = default)
    {
        var existingMap = existingImages.ToDictionary(i => i.RelativePath, i => i, StringComparer.OrdinalIgnoreCase);
        var foundPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var newItems = new List<ImageRecord>();
        var modifiedItems = new List<ImageRecord>();

        // We'll perform a manual walk or use Directory.EnumerateFiles.
        // Given we need to exclude a specific folder, we can filter it easily.
        
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System, // We want to see hidden files maybe? Spec says "Ignore hidden/system files by default"
            ReturnSpecialDirectories = false
        };

        // Note: The spec says "Ignore hidden/system files by default". 
        // Our internal folder .imagecaptionsearch is hidden. 
        // So we should be careful. 
        // However, EnumerationOptions.AttributesToSkip defaults to System | Hidden? Actually no.
        // Let's set it explicitly.
        options.AttributesToSkip = FileAttributes.System | FileAttributes.Hidden;

        // Wait, if I skip hidden files, I won't find .imagecaptionsearch anyway. 
        // But if I want to discover files INSIDE a library, 
        // and the user has hidden folders they WANT to index? 
        // "Ignore hidden/system files by default".
        
        var discoveredFiles = Directory.EnumerateFiles(rootPath, "*.*", options);

        foreach (var filePath in discoveredFiles)
        {
            ct.ThrowIfCancellationRequested();

            var extension = Path.GetExtension(filePath);
            if (!SupportedExtensions.Contains(extension)) continue;

            var relativePath = GetNormalizedRelativePath(rootPath, filePath);
            
            // Extra safety: exclude .imagecaptionsearch just in case it wasn't hidden or we change attributes later
            if (relativePath.StartsWith(".imagecaptionsearch/", StringComparison.OrdinalIgnoreCase) || 
                relativePath.Equals(".imagecaptionsearch", StringComparison.OrdinalIgnoreCase)) continue;

            foundPaths.Add(relativePath);

            var fileInfo = new FileInfo(filePath);
            var sizeBytes = fileInfo.Length;
            var modifiedUtc = fileInfo.LastWriteTimeUtc;

            if (existingMap.TryGetValue(relativePath, out var existing))
            {
                if (IsModified(existing, sizeBytes, modifiedUtc))
                {
                    modifiedItems.Add(existing with 
                    { 
                        Status = ProcessingState.Pending, 
                        SizeBytes = sizeBytes, 
                        ModifiedUtc = modifiedUtc,
                        IsMissing = false,
                        LastProcessedUtc = null,
                        LastError = null
                    });
                }
            }
            else
            {
                newItems.Add(new ImageRecord(
                    Guid.NewGuid().ToString(),
                    relativePath,
                    Path.GetFileName(filePath),
                    extension,
                    sizeBytes,
                    modifiedUtc,
                    DateTime.UtcNow,
                    ProcessingState.Pending
                ));
            }
        }

        var missingIds = existingMap
            .Where(kvp => !foundPaths.Contains(kvp.Key) && !kvp.Value.IsMissing)
            .Select(kvp => kvp.Value.Id)
            .ToList();

        return Task.FromResult(new ScanResult(newItems, modifiedItems, missingIds));
    }

    private static string GetNormalizedRelativePath(string rootPath, string filePath)
    {
        var rel = Path.GetRelativePath(rootPath, filePath);
        return rel.Replace('\\', '/');
    }

    private static bool IsModified(ImageRecord existing, long sizeBytes, DateTime modifiedUtc)
    {
        // Compare with a small tolerance for time? SQLite stores as strings in some cases.
        // But we are using DateTime.
        return existing.SizeBytes != sizeBytes || 
               Math.Abs((existing.ModifiedUtc - modifiedUtc).TotalSeconds) > 1 ||
               existing.IsMissing;
    }
}
