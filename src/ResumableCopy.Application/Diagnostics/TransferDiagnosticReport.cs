using ResumableCopy.Application.Configuration;
using ResumableCopy.Application.Models;
using ResumableCopy.Core.Domain;

namespace ResumableCopy.Application.Diagnostics;

public sealed record TransferDiagnosticReport(
    string ApplicationVersion,
    string OperatingSystem,
    string Framework,
    string SessionId,
    string SourcePath,
    string DestinationPath,
    int FileCount,
    long TotalBytes,
    long CompletedBytes,
    int WorkerCount,
    int ChunkSize,
    int TotalChunks,
    int CompletedChunks,
    CopyState State,
    TimeSpan ElapsedTime,
    string? FailureInformation)
{
    public static TransferDiagnosticReport Create(
        TransferSnapshot snapshot,
        CopyOptions options,
        TimeSpan elapsedTime,
        string? failureInformation = null) =>
        new(
            ApplicationInfo.Version,
            ApplicationInfo.OperatingSystemDescription,
            ApplicationInfo.FrameworkDescription,
            snapshot.SessionId,
            snapshot.SourcePath,
            snapshot.DestinationPath,
            FileCount: 1,
            snapshot.TotalBytes,
            snapshot.BytesCopied,
            options.MaximumWorkers,
            options.ChunkSize,
            snapshot.TotalChunks,
            snapshot.CompletedChunks,
            snapshot.State,
            elapsedTime,
            failureInformation ?? snapshot.ErrorMessage);

    public override string ToString()
    {
        return string.Join(
            Environment.NewLine,
            $"Application version: {ApplicationVersion}",
            $"OS: {OperatingSystem}",
            $"Framework: {Framework}",
            $"Transfer ID: {SessionId}",
            $"Source: {SourcePath}",
            $"Destination: {DestinationPath}",
            $"File count: {FileCount}",
            $"Total size: {TotalBytes} bytes",
            $"Completed bytes: {CompletedBytes}",
            $"Worker count: {WorkerCount}",
            $"Chunk size: {ChunkSize}",
            $"Elapsed time: {ElapsedTime:c}",
            $"State: {State}",
            FailureInformation is null ? null : $"Failure: {FailureInformation}");
    }
}
