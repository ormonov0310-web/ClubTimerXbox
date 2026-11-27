using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace ClubTimerCompatibility;

internal static class SessionStorageCompatibility
{
    internal const string MarkerFile = "session-storage-v1.required";
    internal const string Capability = "ClubTimerXbox;SessionStorage=1";

    internal static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClubTimerXbox");

    internal static bool RequiresSupport(string dataDirectory)
    {
        if (Exists(Path.Combine(dataDirectory, MarkerFile)) ||
            Exists(Path.Combine(dataDirectory, "deferred_checkouts.json")))
            return true;

        // Local builds before the marker was added may already have a saved pause.
        try
        {
            using var stream = File.OpenRead(Path.Combine(dataDirectory, "active_sessions.json"));
            using var json = JsonDocument.Parse(stream);
            foreach (var place in json.RootElement.EnumerateArray())
            {
                if (place.TryGetProperty("PauseState", out var pause) && pause.ValueKind != JsonValueKind.Null)
                    return true;
            }
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        return false;
    }

    internal static void RequireCompatible(string dataDirectory, string programDirectory, string mainExe)
    {
        string dll = Path.Combine(programDirectory, Path.GetFileNameWithoutExtension(mainExe) + ".dll");
        if (Exists(dll) && string.Equals(FileVersionInfo.GetVersionInfo(dll).Comments, Capability, StringComparison.Ordinal))
            return;
        if (!RequiresSupport(dataDirectory))
            return;

        throw new InvalidOperationException(
            "Эта версия не поддерживает сохранённые паузы и отложенные ККМ. " +
            "Установите совместимую версию. Не удаляйте данные клуба или резервные копии.");
    }

    private static bool Exists(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }
}
