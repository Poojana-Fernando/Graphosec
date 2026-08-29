using ResumableCopy.Core.Abstractions;

namespace ResumableCopy.Core.Security;

public static class PlatformSecurityServices
{
    public static IReparsePointInspector CreateReparsePointInspector() =>
        OperatingSystem.IsWindows()
            ? new WindowsReparsePointInspector()
            : NullReparsePointInspector.Instance;

    public static IFileIdentityProvider CreateFileIdentityProvider() =>
        OperatingSystem.IsWindows()
            ? new WindowsFileIdentityProvider()
            : NullFileIdentityProvider.Instance;
}
