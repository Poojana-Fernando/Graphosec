using System.Text.Json;
using ResumableCopy.Application.Abstractions;
using ResumableCopy.Application.Models;
using ResumableCopy.Core.Domain;

namespace ResumableCopy.Application.Services;

public sealed class JsonTransferHistoryStore : ITransferHistoryStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _historyFilePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonTransferHistoryStore(string? historyFilePath = null)
    {
        _historyFilePath = historyFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ResumableCopy",
            "history.json");
    }

    public async Task<IReadOnlyList<TransferHistoryRecord>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_historyFilePath))
            {
                return [];
            }

            await using var stream = File.OpenRead(_historyFilePath);
            var records = await JsonSerializer.DeserializeAsync<List<TransferHistoryRecord>>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);

            return records ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertAsync(TransferHistoryRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = await ReadAllInternalAsync(cancellationToken).ConfigureAwait(false);
            var index = records.FindIndex(existing =>
                string.Equals(existing.SessionId, record.SessionId, StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
            {
                records[index] = record;
            }
            else
            {
                records.Add(record);
            }

            await WriteAllInternalAsync(records, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = await ReadAllInternalAsync(cancellationToken).ConfigureAwait(false);
            var removed = records.RemoveAll(record =>
                string.Equals(record.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));

            if (removed > 0)
            {
                await WriteAllInternalAsync(records, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveFinishedAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = await ReadAllInternalAsync(cancellationToken).ConfigureAwait(false);
            records.RemoveAll(static record => record.State is CopyState.Completed or CopyState.Cancelled or CopyState.Failed);
            await WriteAllInternalAsync(records, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<TransferHistoryRecord>> ReadAllInternalAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_historyFilePath))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(_historyFilePath);
            var records = await JsonSerializer.DeserializeAsync<List<TransferHistoryRecord>>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);

            return records ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task WriteAllInternalAsync(
        IReadOnlyList<TransferHistoryRecord> records,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_historyFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = _historyFilePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, records, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
        }

        File.Move(tempPath, _historyFilePath, overwrite: true);
    }
}
