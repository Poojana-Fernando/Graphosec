namespace ResumableCopy.Core.Domain;

public sealed record CopyJob(
    string SourcePath,
    string DestinationPath,
    CopyOptions? Options = null,
    string? SessionId = null)
{
    public CopyOptions Options { get; init; } = Options ?? new CopyOptions();
}
