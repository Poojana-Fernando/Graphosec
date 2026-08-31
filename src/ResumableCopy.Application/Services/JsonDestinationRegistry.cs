using System.Text.Json;
using ResumableCopy.Application.Abstractions;

namespace ResumableCopy.Application.Services;

public sealed class JsonDestinationRegistry : IDestinationRegistry
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _registryFilePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonDestinationRegistry(string? registryFilePath = null)
    {
        _registryFilePath = registryFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ResumableCopy",
            "destinations.json");
    }

    public async Task RegisterAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var destinations = await ReadAllInternalAsync(cancellationToken).ConfigureAwait(false);
            if (destinations.Any(existing =>
                    string.Equals(existing, destinationPath, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            destinations.Add(destinationPath);
            await WriteAllInternalAsync(destinations, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<string>> GetRegisteredAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadAllInternalAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<string>> ReadAllInternalAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_registryFilePath))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(_registryFilePath);
            var destinations = await JsonSerializer.DeserializeAsync<List<string>>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);

            return destinations ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task WriteAllInternalAsync(
        IReadOnlyList<string> destinations,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_registryFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = _registryFilePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, destinations, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
        }

        File.Move(tempPath, _registryFilePath, overwrite: true);
    }
}
