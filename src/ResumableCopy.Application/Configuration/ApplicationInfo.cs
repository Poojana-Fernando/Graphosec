using System.Reflection;
using System.Runtime.InteropServices;

namespace ResumableCopy.Application.Configuration;

public static class ApplicationInfo
{
    public static string ProductName { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "Graphosec";

    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "1.0.0";

    public static string OperatingSystemDescription { get; } =
        RuntimeInformation.OSDescription;

    public static string FrameworkDescription { get; } =
        RuntimeInformation.FrameworkDescription;
}
