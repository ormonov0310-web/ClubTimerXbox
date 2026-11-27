using System;
using System.IO;

namespace ClubTimerXbox.Services
{
    internal static class AtomicFileStorageService
    {
        public static void WriteAllText(string filePath, string content, bool flushToDisk = false)
        {
            string? folderPath = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(folderPath))
                Directory.CreateDirectory(folderPath);

            string temporaryPath =
                filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                if (flushToDisk)
                {
                    using var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                        FileShare.None, 4096, FileOptions.WriteThrough);
                    using var writer = new StreamWriter(stream);
                    writer.Write(content);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }
                else
                    File.WriteAllText(temporaryPath, content);
                File.Move(temporaryPath, filePath, overwrite: true);
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                catch
                {
                    // A stale temporary file must not hide the original result.
                }
            }
        }
    }
}
