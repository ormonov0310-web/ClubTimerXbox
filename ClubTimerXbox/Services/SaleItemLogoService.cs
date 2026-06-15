using System;
using System.Collections.Generic;
using System.IO;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class SaleItemLogoService
    {
        private static readonly Dictionary<string, string> ExactFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ТОРНАДО"] = "tornado.png",
            ["Летс Го 1 литр"] = "letsgo.png",
            ["Яблоко 1 литр"] = "apple.png",
            ["Султан Чай"] = "sultan-tea.png",
            ["Пико 1 литр"] = "piko.png",
            ["Кола 1 литр"] = "cola.png",
            ["Джойстик"] = "xbox-gamepad.jpg"
        };

        private static readonly (string Token, string FileName)[] TokenFiles =
        {
            ("торнадо", "tornado.png"),
            ("lets", "letsgo.png"),
            ("let's", "letsgo.png"),
            ("летс", "letsgo.png"),
            ("ябл", "apple.png"),
            ("султан", "sultan-tea.png"),
            ("чай", "sultan-tea.png"),
            ("piko", "piko.png"),
            ("пико", "piko.png"),
            ("cola", "cola.png"),
            ("кола", "cola.png"),
            ("джой", "xbox-gamepad.jpg"),
            ("gamepad", "xbox-gamepad.jpg"),
            ("xbox", "xbox-gamepad.jpg")
        };

        public static string? GetLogoPath(SaleItem item)
        {
            string? fileName = GetLogoFileName(item.Name);

            if (fileName is null && item.Type == SaleItemType.Service)
                fileName = "xbox-gamepad.jpg";

            if (fileName is null)
                return null;

            foreach (var root in GetAssetRoots())
            {
                var path = Path.Combine(root, fileName);

                if (File.Exists(path))
                    return path;
            }

            return null;
        }

        private static string? GetLogoFileName(string name)
        {
            name = name.Trim();

            if (ExactFiles.TryGetValue(name, out var exactFile))
                return exactFile;

            var normalized = name.ToLowerInvariant();

            foreach (var (token, fileName) in TokenFiles)
            {
                if (normalized.Contains(token, StringComparison.OrdinalIgnoreCase))
                    return fileName;
            }

            return null;
        }

        private static IEnumerable<string> GetAssetRoots()
        {
            yield return Path.Combine(AppContext.BaseDirectory, "Assets", "ProductLogos");
            yield return Path.Combine(Directory.GetCurrentDirectory(), "Assets", "ProductLogos");
            yield return Path.Combine(Directory.GetCurrentDirectory(), "ClubTimerXbox", "Assets", "ProductLogos");

            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            for (int i = 0; i < 6 && directory is not null; i++)
            {
                yield return Path.Combine(directory.FullName, "Assets", "ProductLogos");
                yield return Path.Combine(directory.FullName, "ClubTimerXbox", "Assets", "ProductLogos");

                directory = directory.Parent;
            }
        }
    }
}
