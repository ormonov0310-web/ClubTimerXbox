using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ClubTimerXbox.Services;

internal static class DurableCheckoutFile
{
    public static void Save<T>(string path, List<T> records)
    {
        // The legacy loaders can hide unreadable data by returning an empty list.
        // A deferred checkout must not turn that fallback into an overwrite.
        try
        {
            if (JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path)) is null)
                throw new InvalidDataException("The existing checkout destination is not a record list.");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }

        AtomicFileStorageService.WriteAllText(path, JsonSerializer.Serialize(records), flushToDisk: true);
    }
}
