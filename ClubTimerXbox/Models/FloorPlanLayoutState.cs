using System.Collections.Generic;

namespace ClubTimerXbox.Models
{
    public enum PlacesDisplayMode
    {
        Classic,
        Alternative
    }

    public class FloorPlanLayoutState
    {
        public PlacesDisplayMode DisplayMode { get; set; } = PlacesDisplayMode.Classic;
        public List<FloorPlanPlacePosition> Positions { get; set; } =
            new List<FloorPlanPlacePosition>();
    }

    public class FloorPlanPlacePosition
    {
        public string PlaceName { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
    }

    internal class FloorPlanLayoutStore
    {
        public Dictionary<string, FloorPlanLayoutState> Clubs { get; set; } =
            new Dictionary<string, FloorPlanLayoutState>();
    }
}
