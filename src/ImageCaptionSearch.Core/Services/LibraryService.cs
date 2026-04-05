using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using ImageCaptionSearch.Core.Interfaces;
using ImageCaptionSearch.Core.Models;

namespace ImageCaptionSearch.Core.Services;

public class LibraryService : ILibraryService
{
    private const int SchemaVersion = 1;

    public LibraryService()
    {
    }

    public async Task InitializeLibraryAsync(Library library, CancellationToken ct = default)
    {
        var internalPath = Path.Combine(library.RootPath, ".imagecaptionsearch");
        
        // 1. Create directory and hide it
        if (!Directory.Exists(internalPath))
        {
            var di = Directory.CreateDirectory(internalPath);
            di.Attributes |= FileAttributes.Hidden;
        }

        var thumbnailsPath = Path.Combine(internalPath, "thumbnails");
        if (!Directory.Exists(thumbnailsPath))
        {
            Directory.CreateDirectory(thumbnailsPath);
        }

        // 2. Initialize Catalog DB
        var catalogPath = Path.Combine(internalPath, "catalog.db");
        await InitializeCatalogDbAsync(catalogPath, ct);

        // 3. Initialize Vectors DB
        var vectorsPath = Path.Combine(internalPath, "vectors.db");
        await InitializeVectorsDbAsync(vectorsPath, ct);
    }

    public Task<LibraryStatus> GetLibraryStatusAsync(Library library, CancellationToken ct = default)
    {
        // Placeholder implementation for now as per M2 requirements
        return Task.FromResult(new LibraryStatus(0, 0, 0, 0, null));
    }

    private async Task InitializeCatalogDbAsync(string dbPath, CancellationToken ct)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync(ct);

        using var transaction = connection.BeginTransaction();

        // Schema Info
        await ExecuteCommandAsync(connection, @"
            CREATE TABLE IF NOT EXISTS schema_info (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );", transaction, ct);

        // Check/Set Version
        var versionStr = await ExecuteScalarAsync<string>(connection, 
            "SELECT value FROM schema_info WHERE key = 'version'", transaction, ct);
        
        if (versionStr == null)
        {
            await ExecuteCommandAsync(connection, 
                "INSERT INTO schema_info (key, value) VALUES ('version', @v)", transaction, ct, 
                new SqliteParameter("@v", SchemaVersion.ToString()));
        }

        // Library Settings
        await ExecuteCommandAsync(connection, @"
            CREATE TABLE IF NOT EXISTS library_settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );", transaction, ct);

        // Images
        await ExecuteCommandAsync(connection, @"
            CREATE TABLE IF NOT EXISTS images (
                id TEXT PRIMARY KEY,
                relative_path TEXT NOT NULL UNIQUE,
                file_name TEXT NOT NULL,
                extension TEXT NOT NULL,
                size_bytes INTEGER NOT NULL,
                modified_utc TEXT NOT NULL,
                created_utc TEXT NULL,
                width INTEGER NULL,
                height INTEGER NULL,
                content_hash TEXT NULL,
                status TEXT NOT NULL,
                last_error TEXT NULL,
                is_missing INTEGER NOT NULL DEFAULT 0,
                discovered_utc TEXT NOT NULL,
                last_processed_utc TEXT NULL,
                thumbnail_rel_path TEXT NULL
            );", transaction, ct);

        // Captions
        await ExecuteCommandAsync(connection, @"
            CREATE TABLE IF NOT EXISTS captions (
                image_id TEXT PRIMARY KEY,
                caption TEXT NOT NULL,
                raw_json TEXT NOT NULL,
                has_human INTEGER NOT NULL,
                vision_model TEXT NOT NULL,
                prompt_version TEXT NOT NULL,
                captioned_utc TEXT NOT NULL,
                FOREIGN KEY(image_id) REFERENCES images(id)
            );", transaction, ct);

        // Faces
        await ExecuteCommandAsync(connection, @"
            CREATE TABLE IF NOT EXISTS faces (
                id TEXT PRIMARY KEY,
                image_id TEXT NOT NULL,
                face_index INTEGER NOT NULL,
                bbox_x REAL NOT NULL,
                bbox_y REAL NOT NULL,
                bbox_width REAL NOT NULL,
                bbox_height REAL NOT NULL,
                detector_model TEXT NOT NULL,
                recognizer_model TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                FOREIGN KEY(image_id) REFERENCES images(id)
            );", transaction, ct);

        // Processing Jobs
        await ExecuteCommandAsync(connection, @"
            CREATE TABLE IF NOT EXISTS processing_jobs (
                image_id TEXT PRIMARY KEY,
                retry_count INTEGER NOT NULL,
                pipeline_state TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                FOREIGN KEY(image_id) REFERENCES images(id)
            );", transaction, ct);

        // FTS for captions
        await ExecuteCommandAsync(connection, @"
            CREATE VIRTUAL TABLE IF NOT EXISTS captions_fts USING fts5(
                caption,
                content='captions',
                content_rowid='image_id'
            );", transaction, ct);

        // Triggers for FTS maintenance
        await ExecuteCommandAsync(connection, @"
            CREATE TRIGGER IF NOT EXISTS captions_ai AFTER INSERT ON captions BEGIN
              INSERT INTO captions_fts(rowid, caption) VALUES (new.image_id, new.caption);
            END;
            CREATE TRIGGER IF NOT EXISTS captions_ad AFTER DELETE ON captions BEGIN
              INSERT INTO captions_fts(captions_fts, rowid, caption) VALUES('delete', old.image_id, old.caption);
            END;
            CREATE TRIGGER IF NOT EXISTS captions_au AFTER UPDATE ON captions BEGIN
              INSERT INTO captions_fts(captions_fts, rowid, caption) VALUES('delete', old.image_id, old.caption);
              INSERT INTO captions_fts(rowid, caption) VALUES (new.image_id, new.caption);
            END;", transaction, ct);

        await transaction.CommitAsync(ct);
    }

    private async Task InitializeVectorsDbAsync(string dbPath, CancellationToken ct)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync(ct);

        using var transaction = connection.BeginTransaction();

        // Image Embeddings
        await ExecuteCommandAsync(connection, @"
            CREATE TABLE IF NOT EXISTS image_embeddings (
                image_id TEXT PRIMARY KEY,
                model_name TEXT NOT NULL,
                dimension INTEGER NOT NULL,
                vector_blob BLOB NOT NULL,
                vector_norm REAL NOT NULL,
                embedded_utc TEXT NOT NULL
            );", transaction, ct);

        // Face Embeddings
        await ExecuteCommandAsync(connection, @"
            CREATE TABLE IF NOT EXISTS face_embeddings (
                face_id TEXT PRIMARY KEY,
                model_name TEXT NOT NULL,
                dimension INTEGER NOT NULL,
                vector_blob BLOB NOT NULL,
                vector_norm REAL NOT NULL,
                embedded_utc TEXT NOT NULL
            );", transaction, ct);

        await transaction.CommitAsync(ct);
    }

    private static async Task ExecuteCommandAsync(SqliteConnection connection, string sql, SqliteTransaction transaction, CancellationToken ct, params SqliteParameter[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<T?> ExecuteScalarAsync<T>(SqliteConnection connection, string sql, SqliteTransaction transaction, CancellationToken ct, params SqliteParameter[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        command.Parameters.AddRange(parameters);
        var result = await command.ExecuteScalarAsync(ct);
        if (result == null || result == DBNull.Value) return default;
        return (T)result;
    }
}
