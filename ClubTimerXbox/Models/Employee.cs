namespace ClubTimerXbox.Models
{
    public class Employee
    {
        public string Name { get; set; } = "";

        // Код входа. Пока простой.
        // Позже можно будет хранить безопаснее.
        public string PinCode { get; set; } = "";

        public bool IsActive { get; set; } = true;
    }
}