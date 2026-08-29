using ResumableCopy.Application.Abstractions;

namespace ResumableCopy.Application.Devices;

public sealed class NullVolumeChangeMonitor : IVolumeChangeMonitor
{
#pragma warning disable CS0067
    public event EventHandler? VolumesChanged;
#pragma warning restore CS0067

    public void Start()
    {
    }

    public void Dispose()
    {
    }
}
