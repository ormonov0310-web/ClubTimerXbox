using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class FloorPlanLayoutService
    {
        private static readonly object Sync = new object();

        private static readonly string FilePath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox",
                "floor-plan-layout.json"
            );

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public static FloorPlanLayoutState LoadCurrent()
        {
            lock (Sync)
            {
                FloorPlanLayoutStore store = LoadStore();
                string key = GetCurrentClubKey();

                if (!store.Clubs.TryGetValue(key, out FloorPlanLayoutState? state) ||
                    state == null)
                {
                    return new FloorPlanLayoutState();
                }

                return CloneAndNormalize(state);
            }
        }

        public static void SaveCurrent(FloorPlanLayoutState state)
        {
            lock (Sync)
            {
                FloorPlanLayoutStore store = LoadStore();
                store.Clubs[GetCurrentClubKey()] = CloneAndNormalize(state);

                AtomicFileStorageService.WriteAllText(
                    FilePath,
                    JsonSerializer.Serialize(store, JsonOptions)
                );
            }
        }

        private static FloorPlanLayoutStore LoadStore()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new FloorPlanLayoutStore();

                return JsonSerializer.Deserialize<FloorPlanLayoutStore>(
                           File.ReadAllText(FilePath),
                           JsonOptions
                       ) ?? new FloorPlanLayoutStore();
            }
            catch
            {
                return new FloorPlanLayoutStore();
            }
        }

        private static string GetCurrentClubKey()
        {
            PcIdentity identity = PcIdentityService.Current;

            if (!string.IsNullOrWhiteSpace(identity.ClubId))
                return "club:" + identity.ClubId.Trim().ToLowerInvariant();

            return "installation:" + identity.InstallationId.Trim();
        }

        private static FloorPlanLayoutState CloneAndNormalize(FloorPlanLayoutState source)
        {
            var result = new FloorPlanLayoutState
            {
                DisplayMode = Enum.IsDefined(source.DisplayMode)
                    ? source.DisplayMode
                    : PlacesDisplayMode.Classic
            };

            foreach (FloorPlanPlacePosition position in
                     (source.Positions ?? new()).Where(item =>
                         !string.IsNullOrWhiteSpace(item.PlaceName)))
            {
                if (result.Positions.Any(item =>
                        item.PlaceName.Equals(
                            position.PlaceName,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                result.Positions.Add(new FloorPlanPlacePosition
                {
                    PlaceName = position.PlaceName.Trim(),
                    X = Math.Clamp(position.X, 0, 1),
                    Y = Math.Clamp(position.Y, 0, 1)
                });
            }

            return result;
        }
    }
}
