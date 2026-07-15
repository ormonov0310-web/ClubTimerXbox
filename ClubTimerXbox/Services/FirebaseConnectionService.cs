namespace ClubTimerXbox.Services
{
    public static class FirebaseConnectionService
    {
        public static bool CanConnect =>
            PcIdentityService.HasAssignedClub &&
            FirebaseAuthService.HasSavedSession;

        public static bool CanSync =>
            CanConnect &&
            FirebaseChannelBindingService.IsCurrentBindingConfirmed;
    }
}
