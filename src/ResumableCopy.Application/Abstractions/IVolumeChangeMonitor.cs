namespace ResumableCopy.Application.Abstractions;

public interface IVolumeChangeMonitor : IDisposable
{
    event EventHandler? VolumesChanged;

    void Start();
}
