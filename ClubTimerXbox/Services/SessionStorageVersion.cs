using System.IO;
using ClubTimerCompatibility;

namespace ClubTimerXbox.Services;

internal static class SessionStorageVersion
{
    // Once written, the requirement also protects settled checkouts and historical pauses.
    internal static void RequireFor(string dataFile)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(dataFile))!;
        string marker = Path.Combine(directory, SessionStorageCompatibility.MarkerFile);
        if (!File.Exists(marker))
            AtomicFileStorageService.WriteAllText(marker, SessionStorageCompatibility.Capability, flushToDisk: true);
    }
}
