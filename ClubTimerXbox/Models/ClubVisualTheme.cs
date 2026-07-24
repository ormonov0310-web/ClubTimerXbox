namespace ClubTimerXbox.Models
{
    public enum ThemeBackdropKind
    {
        None,
        Image,
        Video
    }

    public sealed class ClubVisualTheme
    {
        public required string Id { get; init; }
        public required string DisplayName { get; init; }
        public required string Description { get; init; }
        public ThemeBackdropKind BackdropKind { get; init; }
        public string AssetRelativePath { get; init; } = "";
        public string FallbackImageRelativePath { get; init; } = "";
        public bool UsesGlassSurfaces { get; init; }
    }
}
