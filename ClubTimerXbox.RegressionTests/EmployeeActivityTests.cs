using ClubTimerXbox;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;
using System.Reflection;
using System.Text.Json;

internal static class EmployeeActivityTests
{
    public static void Run()
    {
        int checks = 0;
        void Check(bool result, string message)
        {
            if (!result) throw new InvalidOperationException("Employee activity: " + message);
            checks++;
        }
        void Reject(Action action, string message)
        {
            try { action(); }
            catch (InvalidOperationException) { Check(true, message); return; }
            throw new InvalidOperationException("Employee activity: " + message);
        }
        var active = new Employee { EmployeeId = "A", Name = "Active", PinCode = "2468", IsActive = true };
        var inactive = new Employee { EmployeeId = "B", Name = "Former", PinCode = "1357", IsActive = false };
        Check(EmployeeActivityPolicy.CanParticipate(active), "active account can participate");
        Check(!EmployeeActivityPolicy.CanParticipate(inactive), "inactive account is excluded");
        Check(!EmployeeActivityPolicy.CanParticipate(null), "missing account is excluded");
        string employeeBefore = JsonSerializer.Serialize(inactive);
        Reject(() => EmployeeActivityPolicy.RequireActive(inactive), "inactive account cannot start new activity");
        Reject(() => EmployeeActivityPolicy.RequireActive(null), "unknown account cannot start new activity");
        Check(ReferenceEquals(active, EmployeeActivityPolicy.RequireActive(active)), "active identity is retained");
        Check(JsonSerializer.Serialize(inactive) == employeeBefore, "denial does not modify history or credentials");

        DateTime start = new(2026, 11, 20, 11, 0, 0);
        var shifts = new List<ShiftLogItem>
        {
            new() { EmployeeName = active.Name, StartedAt = start },
            new() { EmployeeName = active.Name, StartedAt = start.AddHours(-1), ClosedAt = start, IsClosed = true },
            new() { EmployeeName = inactive.Name, StartedAt = start }
        };
        string historicalShift = JsonSerializer.Serialize(shifts[1]);
        string otherShift = JsonSerializer.Serialize(shifts[2]);
        Guid originalId = shifts[0].Id;
        var closed = EmployeeActivityPolicy.CloseOpenShifts(shifts, active.Name.ToUpperInvariant(), start.AddHours(2));
        Check(closed.Count == 1 && ReferenceEquals(closed[0], shifts[0]), "disable closes only matching open work");
        Check(shifts[0].IsClosed && shifts[0].ClosedAt == start.AddHours(2), "accrual stops at disable time");
        Check(shifts[0].Id == originalId && shifts[0].StartedAt == start, "shift identity and earned interval retained");
        Check(JsonSerializer.Serialize(shifts[1]) == historicalShift, "old closed shift unchanged");
        Check(JsonSerializer.Serialize(shifts[2]) == otherShift, "another employee unchanged");
        string afterClose = JsonSerializer.Serialize(shifts);
        Check(EmployeeActivityPolicy.CloseOpenShifts(shifts, active.Name, start.AddDays(1)).Count == 0 &&
              JsonSerializer.Serialize(shifts) == afterClose, "retry cannot extend a closed shift");
        shifts = JsonSerializer.Deserialize<List<ShiftLogItem>>(afterClose)!;
        Check(EmployeeActivityPolicy.CloseOpenShifts(shifts, active.Name, start.AddDays(2)).Count == 0 &&
              JsonSerializer.Serialize(shifts) == afterClose, "restart cannot extend a closed shift");
        var legacy = new ShiftLogItem { EmployeeName = active.Name, StartedAt = start,
            ClosedAt = start.AddHours(1), IsClosed = false };
        EmployeeActivityPolicy.CloseOpenShifts(new[] { legacy }, active.Name, start.AddHours(4));
        Check(legacy.IsClosed && legacy.ClosedAt == start.AddHours(1), "existing historical end is not advanced");
        var future = new ShiftLogItem { EmployeeName = active.Name, StartedAt = start.AddHours(1) };
        EmployeeActivityPolicy.CloseOpenShifts(new[] { future }, active.Name, start);
        Check(future.ClosedAt == future.StartedAt, "clock rollback cannot create negative time");

        var workShift = new ShiftLogItem { EmployeeName = active.Name, StartedAt = start };
        Check(EmployeeActivityPolicy.NewPortionWork(active, workShift, start.AddHours(3))?.EmployeeId == active.EmployeeId,
            "new bonus work uses permanent active identity");
        Check(EmployeeActivityPolicy.NewPortionWork(inactive, workShift, start.AddHours(3)) == null,
            "inactive account cannot join a new portion");
        Check(EmployeeActivityPolicy.NewPortionWork(null, workShift, start.AddHours(3)) == null,
            "missing account cannot join a new portion");
        Check(EmployeeActivityPolicy.NewPortionWork(active, workShift, start) == null,
            "zero interval cannot join a portion");
        var day = new OverNormPortionDay { Start = start.Date.AddHours(6), Norm = 5000, Percent = 10 };
        var colleague = new Employee { EmployeeId = "C", Name = "Colleague", IsActive = true };
        var colleagueShift = new ShiftLogItem { EmployeeName = colleague.Name, StartedAt = start };
        var receipts = new[] { new PortionRevenue(Guid.NewGuid(), start.AddHours(3), 5500) };
        OverNormPortionEngine.Accrue(day, start.AddHours(3), receipts, new[]
        {
            EmployeeActivityPolicy.NewPortionWork(active, workShift, start.AddHours(3))!,
            EmployeeActivityPolicy.NewPortionWork(colleague, colleagueShift, start.AddHours(3))!
        });
        Check(day.Portions.Count == 1 && day.Portions[0].Shares.All(share => share.Amount == 25),
            "original share is earned before disable");
        string earnedPortion = JsonSerializer.Serialize(day.Portions[0]);
        active.IsActive = false;
        var nextReceipts = receipts.Append(new PortionRevenue(Guid.NewGuid(), start.AddHours(4), 500));
        OverNormPortionEngine.Accrue(day, start.AddHours(4), nextReceipts, new[]
        {
            EmployeeActivityPolicy.NewPortionWork(colleague, colleagueShift, start.AddHours(4))!
        });
        Check(JsonSerializer.Serialize(day.Portions[0]) == earnedPortion, "disabled employee keeps an already earned share");
        Check(day.Portions.Count == 2 && day.Portions[1].Shares.Single().EmployeeId == colleague.EmployeeId &&
              day.Portions[1].Shares[0].Amount == 50, "only active colleague receives a new share");

        bool Calls(Type type, string method, Type target, string name) =>
            type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                .Where(item => item.Name == method)
                .SelectMany(EmployeeIdentityStartupTests.CalledMethods)
                .Any(item => item.DeclaringType == target && item.Name == name);
        foreach (string method in new[] { "StartShift", "SwitchShift" })
            Check(Calls(typeof(ActionLogService), method, typeof(EmployeeService), "RequireActiveByName"),
                method + " validates activity before new work");
        Check(Calls(typeof(EmployeeService), "SetEmployeeActive", typeof(ActionLogService), "CloseEmployeeOpenShifts"),
            "disabling closes open work through production service");
        Check(Calls(typeof(OverNormPortionService), "GetDay", typeof(EmployeeActivityPolicy), "NewPortionWork"),
            "production portion capture uses active eligibility");
        foreach (string method in new[] { "SynchronizeConfirmedLosses", "SynchronizeConfirmedCashExtras" })
            Check(Calls(typeof(EmployeeRatingService), method, typeof(EmployeeActivityPolicy), "CanParticipate"),
                method + " cannot create a new rating for an inactive account");
        foreach (string method in new[] { "SetBaseRatings", "ResetTo100", "AddManualEvent", "AddRuleEvent" })
            Check(Calls(typeof(EmployeeRatingService), method, typeof(EmployeeService), "RequireActiveByName"),
                method + " blocks new inactive rating mutations");
        Check(Calls(typeof(EmployeeBonusService), "AddOwnerBonus", typeof(EmployeeService), "RequireActiveByName"),
            "new premium cannot be assigned to an inactive account");
        foreach (string method in new[] { "ApplyLateClientReward", "ApplyUnattendedPcPenalty" })
            Check(Calls(typeof(EmployeeNightRatingService), method, typeof(EmployeeActivityPolicy), "CanParticipate"),
                method + " skips inactive accounts");
        foreach (string method in new[] { "EvaluateOpenedShift", "EvaluatePendingRecommendations" })
            Check(Calls(typeof(LateOpeningPenaltyService), method, typeof(EmployeeActivityPolicy), "CanParticipate"),
                method + " skips new automatic inactive penalties");
        foreach (string method in new[] { "StartPrepaid", "StartOpenMode", "AttachSaleToPlace", "SellNowFromMainScreen" })
            Check(Calls(typeof(MainWindow), method, typeof(MainWindow), "EnsureActiveEmployeeActivity"),
                method + " checks activity before committing the operation");
        Check(!Calls(typeof(EmployeeDebtService), "ReconcileAutomatic", typeof(EmployeeActivityPolicy), "CanParticipate") &&
              !Calls(typeof(EmployeeService), "SetEmployeeActive", typeof(EmployeeDebtService), "SetPausedByOwner"),
            "disabling does not pause existing debt contracts");
        Console.WriteLine($"Employee activity: {checks} checks passed (pure data, portions and IL metadata).");
    }
}
