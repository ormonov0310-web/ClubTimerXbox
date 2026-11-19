using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClubTimerXbox.Services;

// This is a single verified incident, not a name-based migration for other employees.
internal static class SalikhEmployeeRecoveryPlan
{
    internal const string Installation = "eccc4fab4273471c8ac4720cd6e96772";
    internal const string EmployeeId = "emp_8651d9d47774433787981a187b5cdb80";
    internal const string FormerEmployeeId = "emp_b50acc8d89e3452486c3600a32b6188b";
    internal const string Name = "\u041c\u0438\u0440\u0431\u0435\u043a";
    internal const string FormerName = "\u041c\u0443\u0445\u0430\u043c\u043c\u0435\u0434";
    internal const string ShiftId = "689c3fbb-c3ba-405f-aa3c-47c8791d18ae";
    internal const string RatingId = "ad130cbb-bdc7-4ef5-ac7d-599184958e05";
    internal const string ReplacementRatingId = "9083a4c5-83aa-4dde-bdd3-159f6fef3f02";
    internal const string RewardPrefix = "acceptance-extra:REVENUE_CONFIRMED_EXTRA:076f38fd5eb126d8d1beac834137f408:";
    internal static readonly string[] Files =
    {
        "logs.json", "payments.json", "cash_records.json", "salary_work_time.json",
        "cash_acceptance_history.json", "cash_reconciliation.json", "employee_rating.json",
        "employees.json", "business_accounting_ledger.json", "employee_payout_transactions.json"
    };

    private static readonly (string Id, int Amount)[] Games =
    {
        ("0180a711-329b-44f4-aaa6-1b4d0315df17", 320),
        ("0f89ebb9-36dd-41c7-9117-e214cce9c3f7", 250),
        ("d038061c-ef8b-4851-93c2-ed6eecc820ad", 340),
        ("eeaee0a9-0783-46d8-9232-76e1d8f91344", 195),
        ("1e17ad52-bb9b-4753-96e3-e9e0c9a5e22e", 100)
    };
    private static readonly (string Id, int Total, int Cash, string? Session)[] Payments =
    {
        ("b17b9556-9b30-4e79-9c82-ca3d8ab6f1fb", 420, 0, Games[0].Id),
        ("26dc9bb6-af0c-42e6-9308-1010ac243e2a", 400, 20, Games[1].Id),
        ("185318ff-9b24-4cb2-8390-a0d20c0236e7", 340, 0, Games[2].Id),
        ("3b677f5b-41f8-4e0b-9673-5ed163ecbcca", 275, 0, Games[3].Id),
        ("562eeab5-552b-493b-b42d-b97becf743ef", 60, 60, Games[4].Id),
        ("cbbf27a8-ee88-4e98-bb68-aaf5260692ff", 40, 40, Games[4].Id),
        ("c982d8fe-5f9d-4c0c-b6dd-7764063d9074", 100, 0, null),
        ("3bc589cf-d7d9-4a0c-b62a-6af187b3f7fc", 100, 100, null)
    };
    private static readonly (string Id, int Amount, int Type)[] Cash =
    {
        ("7eadeb85-853f-4dd2-96ff-8379e6fff57b", 100, 1),
        ("5d458b30-1bc7-4496-b84b-a08de38d7252", 100, 1),
        ("b55c5fba-b502-4844-bdcd-25c9ecb6b069", 320, 0),
        ("f649d1ed-4f8c-4210-ac5a-31fd0be5e6d5", 250, 0),
        ("5a376d9e-c796-4d25-ab58-2c1e9bbe3f8f", 340, 0),
        ("9d4aa368-a5a7-4a14-ba8f-5c09945b5738", 195, 0),
        ("99158c04-6a2e-4fcc-aa1a-fd4f32411878", 60, 0),
        ("f69261d7-0006-48c2-a9e4-ceceb25f6441", 40, 0),
        ("f2e1e93d-2209-43a7-8cb1-9b03eac77680", 100, 1),
        ("eaf50a6f-abc9-4625-85f0-4cd6ecd128fb", 150, 1),
        ("ccb26ecd-84b2-4c08-8bbe-adc2ec3f3672", 80, 1),
        ("05b0107b-71d7-4223-b640-72999bf0a0e3", 21, 4),
        ("709e7c46-9f59-4db0-bd0d-375039ba551d", 100, 4),
        ("01ae10e4-9ccf-4acc-a2bc-13bea4c1dd1f", 11, 4)
    };
    private static readonly string[] PayoutIds =
    {
        "employee-salary:ccfb49683fa04baeac97b6c872371b3e",
        "employee-salary:cd831aa9d4524129b4dae5e36974730d",
        "employee-salary:6436b4888f1a4e658f17207054cdd007"
    };

    internal static Dictionary<string, JsonNode> Build(IReadOnlyDictionary<string, JsonNode> input)
    {
        var data = input.ToDictionary(pair => pair.Key, pair => pair.Value.DeepClone(), StringComparer.Ordinal);
        JsonArray roster = Array(data["employees.json"]);
        Equal(One(roster, "EmployeeId", EmployeeId), "Name", Name);
        JsonObject former = One(roster, "EmployeeId", FormerEmployeeId);
        Equal(former, "Name", FormerName);
        Require(former["IsActive"]?.GetValue<bool>() == false, "former employee active");
        Require(roster.OfType<JsonObject>().Count(row => Text(row, "Name") == Name) == 1, "duplicate name");

        JsonObject ledger = Object(data["business_accounting_ledger.json"]);
        JsonObject months = Object(ledger["Months"]);
        Require(months["2026-11"] is not JsonObject month || month["IsClosed"]?.GetValue<bool>() != true,
            "November payroll is closed");
        var closes = Object(ledger["CloseJournal"]);
        Require(!closes.Select(pair => pair.Value).OfType<JsonObject>().Any(row => Text(row, "MonthKey") == "2026-11"),
            "November close already started");

        JsonObject logs = Object(data["logs.json"]);
        JsonArray shifts = Array(logs["Shifts"]);
        JsonObject shift = One(shifts, "Id", ShiftId);
        Equal(shift, "StartedAt", "2026-11-18T18:19:30.9335568");
        Equal(shift, "ClosedAt", "2026-11-19T01:05:49.6168249");
        Require(shift["IsClosed"]?.GetValue<bool>() == true, "shift not closed");
        Require(!shifts.OfType<JsonObject>().Any(row => Text(row, "EmployeeName") == FormerName &&
            Text(row, "Id") != ShiftId && Text(row, "StartedAt").StartsWith("2026-11", StringComparison.Ordinal)),
            "another November shift belongs to former employee");
        Rename(shift, "EmployeeName");

        JsonArray sessions = Array(logs["GameSessions"]);
        foreach (var spec in Games)
        {
            JsonObject game = One(sessions, "Id", spec.Id);
            Equal(game, "GameIncomeEmployeeName", Name);
            Number(game, "ActualPlayedAmount", spec.Amount);
            Number(game, "RefundAmount", 0);
            Rename(game, "IncomeEmployeeName");
            RenameRole(game, "StartedByEmployeeName");
            RenameRole(game, "ClosedByEmployeeName");
            foreach (JsonObject extra in Array(game["ExtraLines"]).OfType<JsonObject>())
            {
                if (Text(extra, "EmployeeName") != FormerName) continue;
                string at = Text(extra, "CreatedAt");
                Require(at == "2026-11-18T18:40:53.9052945" || at == "2026-11-18T23:22:16.1449618",
                    "unknown session addition");
                Rename(extra, "EmployeeName");
            }
        }
        JsonObject[] sales = sessions.OfType<JsonObject>()
            .SelectMany(game => Array(game["SaleLines"]).OfType<JsonObject>()).ToArray();
        string[] saleIds = { "7fa3925a-da9b-4c04-a157-cc3d4fa7783d", "424a8805-b7f0-4d9b-8603-f9c3ad46d81c",
            "afc46630-2b9c-4fc1-a870-a37f37ad17df", "49105879-5abe-4dd0-b04c-685f5deaa6a6" };
        for (int i = 0; i < saleIds.Length; i++)
        {
            JsonObject sale = One(sales, "Id", saleIds[i]);
            Equal(sale, "PaidByEmployeeId", EmployeeId);
            Equal(sale, "PaidShiftId", ShiftId);
            Number(sale, "TotalAmount", new[] { 100, 100, 50, 80 }[i]);
            Equal(sale, "PaymentRecordId", Payments[new[] { 0, 1, 1, 3 }[i]].Id);
            Rename(sale, "PaidByEmployeeName");
            if (i > 0)
            {
                Equal(sale, "CreatedByEmployeeId", EmployeeId);
                Equal(sale, "CreatedShiftId", ShiftId);
                Rename(sale, "CreatedByEmployeeName");
                Rename(sale, "EmployeeName");
            }
            else
                Equal(sale, "CreatedByEmployeeId", "emp_2783e3e84e5544749308c80bf0354bc7");
            if (Text(sale, "DebtResponsibleShiftId") == ShiftId)
                Rename(sale, "DebtResponsibleEmployeeName");
        }

        JsonArray payments = Array(data["payments.json"]);
        foreach (var spec in Payments)
        {
            JsonObject payment = One(payments, "Id", spec.Id);
            Number(payment, "TotalAmount", spec.Total);
            Number(payment, "CashAmount", spec.Cash);
            Number(payment, "MBankAmount", spec.Total - spec.Cash);
            Equal(payment, "GameSessionId", spec.Session ?? "");
            Rename(payment, "EmployeeName");
        }
        JsonArray cash = Array(data["cash_records.json"]);
        for (int i = 0; i < Cash.Length; i++)
        {
            var spec = Cash[i];
            JsonObject row = One(cash, "Id", spec.Id);
            Number(row, "Amount", spec.Amount);
            Number(row, "Type", spec.Type);
            string? sessionId = i is >= 2 and <= 7 ? Games[new[] { 0, 1, 2, 3, 4, 4 }[i - 2]].Id
                : i is >= 8 and <= 10 ? Games[new[] { 0, 1, 3 }[i - 8]].Id : null;
            Equal(row, "GameSessionId", sessionId ?? "");
            Equal(row, "PaymentRecordId", i is >= 2 and <= 7 ? Payments[i - 2].Id : "");
            Require(Text(row, "BusinessDateKey") == "2026-11-18", "cash business day changed");
            Rename(row, "EmployeeName");
            Rename(row, "IncomeEmployeeName");
            if (i < 11) continue;
            Rename(row, "RelatedEmployeeName");
            Equal(row, "SalaryMonthKey", "2026-11");
            string operation = PayoutIds[i - 11];
            Require(Text(row, "Description").Contains("[salary-payment:" + operation + ":salary:0]", StringComparison.Ordinal),
                "salary cash marker changed");
            JsonObject payout = One(Array(data["employee_payout_transactions.json"]), "OperationId", operation);
            Equal(payout, "EmployeeId", EmployeeId);
            Equal(payout, "EmployeeName", Name);
            Number(payout, "TotalAmount", spec.Amount);
            Number(payout, "SalaryAmount", spec.Amount);
            Require(payout["IsCompleted"]?.GetValue<bool>() == true, "payout incomplete");
            JsonObject payroll = Object(Object(ledger["PayrollPayments"])[operation + ":salary"]);
            Require(payroll["IsCompleted"]?.GetValue<bool>() == true, "payroll incomplete");
            JsonObject allocation = One(Array(payroll["Allocations"]), "EmployeeName", Name);
            Equal(allocation, "SourceMonthKey", "2026-11");
            Number(allocation, "Amount", spec.Amount);
        }
        RejectUnknownNovemberRows(cash, Cash.Select(row => row.Id));
        RejectUnknownNovemberRows(payments, Payments.Select(row => row.Id));

        JsonArray times = Array(Object(data["salary_work_time.json"])["Items"]);
        JsonObject oldTime = One(times.OfType<JsonObject>().Where(row => Text(row, "MonthKey") == "2026-11"),
            "EmployeeName", FormerName);
        Number(oldTime, "CarriedHours", 0);
        Number(oldTime, "RawAnchorHours", 0);
        double oldHours = oldTime["ProtectedHours"]!.Deserialize<double>();
        Require(oldHours == 0 || Math.Abs(oldHours - 6.661819058972222) < 0.000000001, "protected hours changed");
        foreach (string field in new[] { "LastRawHours", "ProtectedHours", "LastSavedProtectedHours" })
        {
            Require(Math.Abs(oldTime[field]!.Deserialize<double>() - oldHours) < 0.000000001, "work checkpoint changed");
            oldTime[field] = 0;
        }
        JsonObject newTime = One(times.OfType<JsonObject>().Where(row => Text(row, "MonthKey") == "2026-11"),
            "EmployeeName", Name);
        Number(newTime, "CarriedHours", 0);
        Number(newTime, "RawAnchorHours", 0);

        JsonArray acceptances = Array(data["cash_acceptance_history.json"]);
        Rename(One(acceptances, "Id", "7912f479-c932-4573-979f-4d4d0bcbc935"), "CheckedByEmployeeName");
        Rename(One(acceptances, "Id", "12aaed54-24c1-4841-8bbf-51d22795f625"), "ResponsibleEmployeeName");
        JsonArray reconciliation = Array(data["cash_reconciliation.json"]);
        JsonObject contribution = One(reconciliation.OfType<JsonObject>()
            .SelectMany(row => Array(row["ExtraContributions"]).OfType<JsonObject>()),
            "Id", "087e0af5-1815-42d5-9876-b7b520ced1c6");
        Equal(contribution, "InvestigationId", "076f38fd-5eb1-26d8-d1be-ac834137f408");
        Number(contribution, "OriginalAmount", 12);
        Rename(contribution, "EmployeeName");
        Rename(One(reconciliation, "Id", "f3ae8e15-d1f2-415a-a92b-ee8f53185c71"), "ResponsibleEmployeeName");
        RepairRating(Object(data["employee_rating.json"]));

        return data.Where(pair => !JsonNode.DeepEquals(pair.Value, input[pair.Key]))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private static void RepairRating(JsonObject rating)
    {
        JsonArray events = Array(rating["Events"]);
        JsonObject original = One(events, "Id", RatingId);
        Equal(original, "EmployeeId", FormerEmployeeId);
        Equal(original, "SourceId", RewardPrefix + FormerEmployeeId);
        Equal(original, "RuleCode", "REVENUE_CONFIRMED_EXTRA");
        Equal(original, "EffectiveFrom", "2026-11-19T20:09:00.94006");
        Equal(original, "ScheduledUntil", "2026-11-22T20:09:00.94006");
        Number(original, "Branch", 1);
        Number(original, "Direction", 1);
        Number(original, "ChangePercent", 5);
        Number(original, "BasePercentAtCreation", 74);
        Number(original, "TargetPercent", 79);
        Number(original, "CompensationAmount", 0);
        var replacements = events.OfType<JsonObject>()
            .Where(row => Text(row, "SourceId") == RewardPrefix + EmployeeId).ToList();
        if (replacements.Count > 0)
        {
            Require(replacements.Count == 1, "duplicate corrected reward");
            JsonObject replacement = replacements.Single();
            Equal(replacement, "Id", ReplacementRatingId);
            Equal(replacement, "EmployeeId", EmployeeId);
            Number(replacement, "TargetPercent", 105);
            Number(original, "Status", 2);
            Equal(original, "EndedAt", Text(original, "EffectiveFrom"));
            return;
        }
        Number(original, "Status", 0);
        Require(original["EndedAt"] == null, "reward already resolved");
        JsonObject profile = One(Array(rating["Profiles"]), "EmployeeId", EmployeeId);
        JsonObject basis = Array(profile["BaseVersions"]).OfType<JsonObject>()
            .Where(row => string.CompareOrdinal(Text(row, "EffectiveFrom"), Text(original, "EffectiveFrom")) <= 0)
            .OrderByDescending(row => Text(row, "EffectiveFrom"), StringComparer.Ordinal).First();
        Number(basis, "RevenuePercent", 100);
        JsonObject corrected = (JsonObject)original.DeepClone();
        corrected["Id"] = ReplacementRatingId;
        corrected["EmployeeId"] = EmployeeId;
        corrected["EmployeeName"] = Name;
        corrected["SourceId"] = RewardPrefix + EmployeeId;
        corrected["BasePercentAtCreation"] = 100;
        corrected["TargetPercent"] = 105;
        corrected["ResolutionNote"] = "SAL-EMP-RENAME-002: corrected employee identity; original effective interval retained.";
        original["Status"] = 2;
        original["EndedAt"] = original["EffectiveFrom"]!.DeepClone();
        if (original.ContainsKey("EffectiveUntil")) original["EffectiveUntil"] = original["EffectiveFrom"]!.DeepClone();
        original["ResolutionNote"] = "SAL-EMP-RENAME-002: wrong employee; replaced by " + ReplacementRatingId;
        events.Add(corrected);
    }

    private static void RejectUnknownNovemberRows(JsonArray rows, IEnumerable<string> knownIds)
    {
        var known = knownIds.ToHashSet(StringComparer.Ordinal);
        Require(!rows.OfType<JsonObject>().Any(row => !known.Contains(Text(row, "Id")) &&
            Text(row, "CreatedAt").StartsWith("2026-11", StringComparison.Ordinal) &&
            new[] { "EmployeeName", "IncomeEmployeeName", "RelatedEmployeeName" }.Any(field => Text(row, field) == FormerName)),
            "unreviewed November attribution");
    }

    private static void RenameRole(JsonObject row, string field)
    {
        if (Text(row, field) == FormerName) Rename(row, field);
    }
    private static void Rename(JsonObject row, string field)
    {
        string value = Text(row, field);
        Require(value == FormerName || value == Name, "identity changed: " + field);
        row[field] = Name;
    }
    private static void Equal(JsonObject row, string field, string value) =>
        Require(Text(row, field) == value, "record changed: " + field);
    private static void Number(JsonObject row, string field, double value) =>
        Require(row[field] != null && row[field]!.Deserialize<double>() == value, "value changed: " + field);
    internal static string Text(JsonObject row, string field) => row[field]?.GetValue<string>() ?? "";
    internal static JsonObject Object(JsonNode? node) => node as JsonObject ?? throw new InvalidOperationException("Recovery: missing object.");
    internal static JsonArray Array(JsonNode? node) => node as JsonArray ?? throw new InvalidOperationException("Recovery: missing list.");
    private static JsonObject One(JsonArray rows, string field, string value) => One(rows.OfType<JsonObject>(), field, value);
    private static JsonObject One(IEnumerable<JsonObject> rows, string field, string value)
    {
        var found = rows.Where(row => Text(row, field) == value).ToArray();
        Require(found.Length == 1, "missing or duplicate record: " + value);
        return found[0];
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("SAL-EMP-RENAME-002: " + message);
    }
}
