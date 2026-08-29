namespace ResumableCopy.Core.Configuration;

public sealed class DeviceProbeOptions
{
    public static readonly TimeSpan DefaultReadinessCacheTtl = TimeSpan.FromSeconds(2);

    public static readonly TimeSpan DefaultNotReadyReadinessCacheTtl = TimeSpan.FromMilliseconds(400);

    public TimeSpan ReadinessCacheTtl { get; set; } = DefaultReadinessCacheTtl;

    public TimeSpan NotReadyReadinessCacheTtl { get; set; } = DefaultNotReadyReadinessCacheTtl;
}
