using System;
using System.Collections.Generic;
using System.Globalization;
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

    public async Task<LibraryStatus> GetLibraryStatusAsync(Library library, CancellationToken ct = default)
    {
        var catalogPath = Path.Combine(library.RootPath, ".imagecaptionsearch", "catalog.db");
        if (!File.Exists(catalogPath)) return new LibraryStatus(0, 0, 0, 0, null);

        using var connection = new SqliteConnection($"Data Source={catalogPath}");
        await connection.OpenAsync(ct);

        var total = await ExecuteScalarAsync<long>(connection, "SELECT COUNT(*) FROM images WHERE is_missing = 0", null, ct);
        var indexed = await ExecuteScalarAsync<long>(connection, "SELECT COUNT(*) FROM images WHERE status = 'Completed' AND is_missing = 0", null, ct);
        var pending = await ExecuteScalarAsync<long>(connection, "SELECT COUNT(*) FROM images WHERE status NOT IN ('Completed', 'Failed', 'Missing') AND is_missing = 0", null, ct);
        var failed = await ExecuteScalarAsync<long>(connection, "SELECT COUNT(*) FROM images WHERE status = 'Failed' AND is_missing = 0", null, ct);

        return new LibraryStatus((int)total, (int)indexed, (int)pending, (int)failed, null);
    }

    public async Task<IReadOnlyList<ImageRecord>> GetImagesAsync(Library library, CancellationToken ct = default)
    {
        var catalogPath = Path.Combine(library.RootPath, ".imagecaptionsearch", "catalog.db");
        using var connection = new SqliteConnection($"Data Source={catalogPath}");
        await connection.OpenAsync(ct);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, relative_path, file_name, extension, size_bytes, modified_utc, discovered_utc, status, is_missing, width, height, last_processed_utc, thumbnail_rel_path, last_error FROM images";
        
        var results = new List<ImageRecord>();
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new ImageRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt64(4),
                DateTime.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
                DateTime.Parse(reader.GetString(6), CultureInfo.InvariantCulture),
                Enum.Parse<ProcessingState>(reader.GetString(7)),
                reader.GetInt32(8) != 0,
                reader.IsDBNull(9) ? null : reader.GetInt32(9),
                reader.IsDBNull(10) ? null : reader.GetInt32(10),
                reader.IsDBNull(11) ? null : DateTime.Parse(reader.GetString(11), CultureInfo.InvariantCulture),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13)
            ));
        }
        return results;
    }

    public async Task UpsertImagesAsync(Library library, IEnumerable<ImageRecord> images, CancellationToken ct = default)
    {
        var catalogPath = Path.Combine(library.RootPath, ".imagecaptionsearch", "catalog.db");
        using var connection = new SqliteConnection($"Data Source={catalogPath}");
        await connection.OpenAsync(ct);

        using var transaction = connection.BeginTransaction();
        foreach (var img in images)
        {
            await ExecuteCommandAsync(connection, @"
                INSERT INTO images (id, relative_path, file_name, extension, size_bytes, modified_utc, status, is_missing, discovered_utc, width, height, last_processed_utc, thumbnail_rel_path, last_error)
                VALUES (@id, @rel, @name, @ext, @size, @mod, @status, @miss, @disc, @w, @h, @proc, @thumb, @err)
                ON CONFLICT(relative_path) DO UPDATE SET
                    size_bytes = excluded.size_bytes,
                    modified_utc = excluded.modified_utc,
                    status = excluded.status,
                    is_missing = excluded.is_missing,
                    last_processed_utc = excluded.last_processed_utc,
                    thumbnail_rel_path = excluded.thumbnail_rel_path,
                    last_error = excluded.last_error",
                transaction, ct,
                new SqliteParameter("@id", img.Id),
                new SqliteParameter("@rel", img.RelativePath),
                new SqliteParameter("@name", img.FileName),
                new SqliteParameter("@ext", img.Extension),
                new SqliteParameter("@size", img.SizeBytes),
                new SqliteParameter("@mod", img.ModifiedUtc.ToString("o", CultureInfo.InvariantCulture)),
                new SqliteParameter("@status", img.Status.ToString()),
                new SqliteParameter("@miss", img.IsMissing ? 1 : 0),
                new SqliteParameter("@disc", img.DiscoveredUtc.ToString("o", CultureInfo.InvariantCulture)),
                new SqliteParameter("@w", (object?)img.Width ?? DBNull.Value),
                new SqliteParameter("@h", (object?)img.Height ?? DBNull.Value),
                new SqliteParameter("@proc", (object?)img.LastProcessedUtc?.ToString("o", CultureInfo.InvariantCulture) ?? DBNull.Value),
                new SqliteParameter("@thumb", (object?)img.ThumbnailRelPath ?? DBNull.Value),
                new SqliteParameter("@err", (object?)img.LastError ?? DBNull.Value)
            );
        }
        await transaction.CommitAsync(ct);
    }

    public async Task MarkMissingAsync(Library library, IEnumerable<string> imageIds, CancellationToken ct = default)
    {
        var catalogPath = Path.Combine(library.RootPath, ".imagecaptionsearch", "catalog.db");
        using var connection = new SqliteConnection($"Data Source={catalogPath}");
        await connection.OpenAsync(ct);

        using var transaction = connection.BeginTransaction();
        foreach (var id in imageIds)
        {
            await ExecuteCommandAsync(connection, 
                "UPDATE images SET is_missing = 1, status = @s WHERE id = @id",
                transaction, ct,
                new SqliteParameter("@s", ProcessingState.Missing.ToString()),
                new SqliteParameter("@id", id));
        }
        await transaction.CommitAsync(ct);
    }

    public async Task SaveCaptionAsync(Library library, CaptionRecord caption, CancellationToken ct = default)
    {
        var catalogPath = Path.Combine(library.RootPath, ".imagecaptionsearch", "catalog.db");
        using var connection = new SqliteConnection($"Data Source={catalogPath}");
        await connection.OpenAsync(ct);

        using var transaction = connection.BeginTransaction();
        await ExecuteCommandAsync(connection, @"
            INSERT INTO captions (image_id, caption, raw_json, has_human, vision_model, prompt_version, captioned_utc)
            VALUES (@id, @cap, @raw, @human, @model, @prompt, @utc)
            ON CONFLICT(image_id) DO UPDATE SET 
                caption=excluded.caption, 
                raw_json=excluded.raw_json, 
                has_human=excluded.has_human, 
                captioned_utc=excluded.captioned_utc",
            transaction, ct,
            new SqliteParameter("@id", caption.ImageId),
            new SqliteParameter("@cap", caption.Caption),
            new SqliteParameter("@raw", caption.RawJson),
            new SqliteParameter("@human", caption.HasHuman ? 1 : 0),
            new SqliteParameter("@model", caption.VisionModel),
            new SqliteParameter("@prompt", caption.PromptVersion),
            new SqliteParameter("@utc", caption.CaptionedUtc.ToString("o", CultureInfo.InvariantCulture))
        );
        await transaction.CommitAsync(ct);
    }

    public async Task SaveEmbeddingAsync(Library library, EmbeddingRecord embedding, CancellationToken ct = default)
    {
        var vectorsPath = Path.Combine(library.RootPath, ".imagecaptionsearch", "vectors.db");
        using var connection = new SqliteConnection($"Data Source={vectorsPath}");
        await connection.OpenAsync(ct);

        using var transaction = connection.BeginTransaction();
        var vectorBytes = new byte[embedding.Vector.Length * sizeof(float)];
        Buffer.BlockCopy(embedding.Vector, 0, vectorBytes, 0, vectorBytes.Length);

        await ExecuteCommandAsync(connection, @"
            INSERT INTO image_embeddings (image_id, model_name, dimension, vector_blob, vector_norm, embedded_utc)
            VALUES (@id, @model, @dim, @blob, @norm, @utc)
            ON CONFLICT(image_id) DO UPDATE SET
                model_name=excluded.model_name,
                dimension=excluded.dimension,
                vector_blob=excluded.vector_blob,
                vector_norm=excluded.vector_norm,
                embedded_utc=excluded.embedded_utc",
            transaction, ct,
            new SqliteParameter("@id", embedding.ParentId),
            new SqliteParameter("@model", embedding.ModelName),
            new SqliteParameter("@dim", embedding.Dimension),
            new SqliteParameter("@blob", vectorBytes),
            new SqliteParameter("@norm", embedding.VectorNorm),
            new SqliteParameter("@utc", embedding.EmbeddedUtc.ToString("o", CultureInfo.InvariantCulture))
        );
        await transaction.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<EmbeddingRecord>> GetEmbeddingsAsync(Library library, string? modelId = null, CancellationToken ct = default)
    {
        var vectorsPath = Path.Combine(library.RootPath, ".imagecaptionsearch", "vectors.db");
        using var connection = new SqliteConnection($"Data Source={vectorsPath}");
        await connection.OpenAsync(ct);

        using var command = connection.CreateCommand();
        if (modelId != null)
        {
            command.CommandText = "SELECT image_id, model_name, dimension, vector_blob, vector_norm, embedded_utc FROM image_embeddings WHERE model_name = @model";
            command.Parameters.AddWithValue("@model", modelId);
        }
        else
        {
            command.CommandText = "SELECT image_id, model_name, dimension, vector_blob, vector_norm, embedded_utc FROM image_embeddings";
        }

        var results = new List<EmbeddingRecord>();
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var blob = (byte[])reader[3];
            var floatArray = new float[blob.Length / sizeof(float)];
            Buffer.BlockCopy(blob, 0, floatArray, 0, blob.Length);

            results.Add(new EmbeddingRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                floatArray,
                reader.GetDouble(4),
                DateTime.Parse(reader.GetString(5), CultureInfo.InvariantCulture)
            ));
        }
        return results;
    }

    public async Task SaveFacesAsync(Library library, string imageId, IReadOnlyList<FaceDetectionResult> faces, string detectorModel, string recognizerModel, CancellationToken ct = default)
    {
        var internalPath = Path.Combine(library.RootPath, ".imagecaptionsearch");
        var catalogPath = Path.Combine(internalPath, "catalog.db");
        var vectorsPath = Path.Combine(internalPath, "vectors.db");

        // 1. Update Catalog DB
        using (var catalogConn = new SqliteConnection($"Data Source={catalogPath}"))
        {
            await catalogConn.OpenAsync(ct);
            using var transaction = catalogConn.BeginTransaction();

            // Clear old faces for this image if re-processing
            await ExecuteCommandAsync(catalogConn, "DELETE FROM faces WHERE image_id = @id", transaction, ct, new SqliteParameter("@id", imageId));

            foreach (var face in faces)
            {
                var faceId = Guid.NewGuid().ToString();
                await ExecuteCommandAsync(catalogConn, @"
                    INSERT INTO faces (id, image_id, face_index, bbox_x, bbox_y, bbox_width, bbox_height, detector_model, recognizer_model, created_utc)
                    VALUES (@id, @img, @idx, @x, @y, @w, @h, @det, @rec, @utc)",
                    transaction, ct,
                    new SqliteParameter("@id", faceId),
                    new SqliteParameter("@img", imageId),
                    new SqliteParameter("@idx", face.FaceIndex),
                    new SqliteParameter("@x", face.BBoxX),
                    new SqliteParameter("@y", face.BBoxY),
                    new SqliteParameter("@w", face.BBoxWidth),
                    new SqliteParameter("@h", face.BBoxHeight),
                    new SqliteParameter("@det", detectorModel),
                    new SqliteParameter("@rec", recognizerModel),
                    new SqliteParameter("@utc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture))
                );

                // 2. Update Vectors DB for EACH face
                using (var vectorConn = new SqliteConnection($"Data Source={vectorsPath}"))
                {
                    await vectorConn.OpenAsync(ct);
                    var vectorBytes = new byte[face.Vector.Length * sizeof(float)];
                    Buffer.BlockCopy(face.Vector, 0, vectorBytes, 0, vectorBytes.Length);

                    double norm = 0;
                    foreach (var v in face.Vector) norm += v * v;
                    norm = Math.Sqrt(norm);

                    await ExecuteCommandAsync(vectorConn, @"
                        INSERT INTO face_embeddings (face_id, model_name, dimension, vector_blob, vector_norm, embedded_utc)
                        VALUES (@id, @model, @dim, @blob, @norm, @utc)",
                        null, ct,
                        new SqliteParameter("@id", faceId),
                        new SqliteParameter("@model", recognizerModel),
                        new SqliteParameter("@dim", face.Vector.Length),
                        new SqliteParameter("@blob", vectorBytes),
                        new SqliteParameter("@norm", norm),
                        new SqliteParameter("@utc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture))
                    );
                }
            }

            await transaction.CommitAsync(ct);
        }
    }

    public async Task<IReadOnlyList<FaceRecord>> GetFacesAsync(Library library, string imageId, CancellationToken ct = default)
    {
        var catalogPath = Path.Combine(library.RootPath, ".imagecaptionsearch", "catalog.db");
        using var connection = new SqliteConnection($"Data Source={catalogPath}");
        await connection.OpenAsync(ct);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, image_id, face_index, bbox_x, bbox_y, bbox_width, bbox_height, detector_model, recognizer_model, created_utc FROM faces WHERE image_id = @img";
        command.Parameters.AddWithValue("@img", imageId);

        var results = new List<FaceRecord>();
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new FaceRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetDouble(3),
                reader.GetDouble(4),
                reader.GetDouble(5),
                reader.GetDouble(6),
                reader.GetString(7),
                reader.GetString(8),
                DateTime.Parse(reader.GetString(9), CultureInfo.InvariantCulture)
            ));
        }
        return results;
    }

    public async Task<IReadOnlyList<FaceEmbedding>> GetFaceEmbeddingsAsync(Library library, string? modelId = null, CancellationToken ct = default)
    {
        var vectorsPath = Path.Combine(library.RootPath, ".imagecaptionsearch", "vectors.db");
        using var connection = new SqliteConnection($"Data Source={vectorsPath}");
        await connection.OpenAsync(ct);

        using var command = connection.CreateCommand();
        if (modelId != null)
        {
            command.CommandText = "SELECT face_id, model_name, dimension, vector_blob, vector_norm FROM face_embeddings WHERE model_name = @model";
            command.Parameters.AddWithValue("@model", modelId);
        }
        else
        {
            command.CommandText = "SELECT face_id, model_name, dimension, vector_blob, vector_norm FROM face_embeddings";
        }

        var results = new List<FaceEmbedding>();
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var blob = (byte[])reader[3];
            var floatArray = new float[blob.Length / sizeof(float)];
            Buffer.BlockCopy(blob, 0, floatArray, 0, blob.Length);

            results.Add(new FaceEmbedding(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                floatArray,
                reader.GetDouble(4)
            ));
        }
        return results;
    }

    public async Task<IReadOnlyList<ProcessingJob>> GetActiveJobsAsync(Library library, CancellationToken ct = default)
    {
        var catalogPath = Path.Combine(library.RootPath, ".imagecaptionsearch", "catalog.db");
        using var connection = new SqliteConnection($"Data Source={catalogPath}");
        await connection.OpenAsync(ct);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT image_id, retry_count, pipeline_state, updated_utc FROM processing_jobs";

        var results = new List<ProcessingJob>();
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new ProcessingJob(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetString(2),
                DateTime.Parse(reader.GetString(3), CultureInfo.InvariantCulture)
            ));
        }
        return results;
    }

    public async Task UpsertJobAsync(Library library, ProcessingJob job, CancellationToken ct = default)
    {
        var catalogPath = Path.Combine(library.RootPath, ".imagecaptionsearch", "catalog.db");
        using var connection = new SqliteConnection($"Data Source={catalogPath}");
        await connection.OpenAsync(ct);

        await ExecuteCommandAsync(connection, @"
            INSERT INTO processing_jobs (image_id, retry_count, pipeline_state, updated_utc)
            VALUES (@id, @retry, @state, @utc)
            ON CONFLICT(image_id) DO UPDATE SET
                retry_count = excluded.retry_count,
                pipeline_state = excluded.pipeline_state,
                updated_utc = excluded.updated_utc",
            null, ct,
            new SqliteParameter("@id", job.ImageId),
            new SqliteParameter("@retry", job.RetryCount),
            new SqliteParameter("@state", job.PipelineState),
            new SqliteParameter("@utc", job.UpdatedUtc.ToString("o", CultureInfo.InvariantCulture))
        );
    }

    public async Task RemoveJobAsync(Library library, string imageId, CancellationToken ct = default)
    {
        var catalogPath = Path.Combine(library.RootPath, ".imagecaptionsearch", "catalog.db");
        using var connection = new SqliteConnection($"Data Source={catalogPath}");
        await connection.OpenAsync(ct);

        await ExecuteCommandAsync(connection, 
            "DELETE FROM processing_jobs WHERE image_id = @id",
            null, ct,
            new SqliteParameter("@id", imageId));
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
                new SqliteParameter("@v", SchemaVersion.ToString(CultureInfo.InvariantCulture)));
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
                image_id UNINDEXED
            );", transaction, ct);

        // Triggers for FTS maintenance
        await ExecuteCommandAsync(connection, @"
            CREATE TRIGGER IF NOT EXISTS captions_ai AFTER INSERT ON captions BEGIN
              INSERT INTO captions_fts(image_id, caption) VALUES (new.image_id, new.caption);
            END;
            CREATE TRIGGER IF NOT EXISTS captions_ad AFTER DELETE ON captions BEGIN
              DELETE FROM captions_fts WHERE image_id = old.image_id;
            END;
            CREATE TRIGGER IF NOT EXISTS captions_au AFTER UPDATE ON captions BEGIN
              UPDATE captions_fts SET caption = new.caption WHERE image_id = new.image_id;
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

    private static async Task ExecuteCommandAsync(SqliteConnection connection, string sql, SqliteTransaction? transaction, CancellationToken ct, params SqliteParameter[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<T?> ExecuteScalarAsync<T>(SqliteConnection connection, string sql, SqliteTransaction? transaction, CancellationToken ct, params SqliteParameter[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        command.Parameters.AddRange(parameters);
        var result = await command.ExecuteScalarAsync(ct);
        if (result == null || result == DBNull.Value) return default;
        return (T)Convert.ChangeType(result, typeof(T), CultureInfo.InvariantCulture);
    }
}
