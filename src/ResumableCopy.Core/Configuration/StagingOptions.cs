namespace ResumableCopy.Core.Configuration;

public sealed class StagingOptions
{
    public const string SectionName = "Staging";

    public string CacheDirectoryName { get; set; } = ".copycache";
}
