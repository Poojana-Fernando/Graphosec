using ResumableCopy.Application.Models;

namespace ResumableCopy.Application.Abstractions;

public interface IDriveEnumerationService
{
    IReadOnlyList<DriveInfoSnapshot> GetAvailableDrives();
}
