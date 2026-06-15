namespace ClubTimerXbox.Models
{
    public class PcIdentity
    {
        public string InstallationId { get; set; } = "";

        public string ClubId { get; set; } = "";

        public string ClubName { get; set; } = "";

        public bool IsActivated { get; set; }

        public string ActivatedAt { get; set; } = "";
    }
}
