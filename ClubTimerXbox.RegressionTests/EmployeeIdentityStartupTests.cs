using ClubTimerXbox.Models;
using ClubTimerXbox.Services;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;

internal static class EmployeeIdentityStartupTests
{
    private static readonly string Adilet = "\u0410\u0434\u0438\u043b\u0435\u0442";
    private static readonly string Mirbek = "\u041c\u0438\u0440\u0431\u0435\u043a";
    private static readonly string Muhammed = "\u041c\u0443\u0445\u0430\u043c\u043c\u0435\u0434";
    private static readonly Dictionary<short, OpCode> Opcodes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!)
        .GroupBy(opcode => opcode.Value)
        .ToDictionary(group => group.Key, group => group.First());

    public static void Run()
    {
        int checks = 0;
        var failures = new List<string>();
        void Check(bool result, string message)
        {
            checks++;
            if (!result) failures.Add(message);
        }

        var defaults = new List<Employee>
        {
            new() { Name = Adilet, PinCode = "1111", IsActive = true },
            new() { Name = "Other", PinCode = "2222", IsActive = true }
        };
        string defaultsBefore = JsonSerializer.Serialize(defaults);
        var initial = EmployeeRosterInitialization.Initialize(new(), defaults);
        Check(initial.Count == 2, "empty installation receives initial employees");
        Check(initial.Select(item => item.Name).SequenceEqual(defaults.Select(item => item.Name)),
            "initial names retained");
        Check(initial.Select(item => item.PinCode).SequenceEqual(defaults.Select(item => item.PinCode)),
            "initial credentials retained");
        Check(initial.All(item => item.IsActive), "initial active states retained");
        Check(initial.Zip(defaults).All(pair => !ReferenceEquals(pair.First, pair.Second)),
            "templates are cloned, not shared with the saved roster");
        Check(initial.All(item => string.IsNullOrEmpty(item.EmployeeId)),
            "existing storage normalization remains responsible for new IDs");
        initial[0].Name = "Changed";
        initial[0].PinCode = "9999";
        Check(JsonSerializer.Serialize(defaults) == defaultsBefore, "initial edits cannot change templates");

        var saved = new List<Employee>
        {
            new() { EmployeeId = "original-adilet-id", Name = Mirbek, PinCode = "2468", IsActive = true },
            new() { EmployeeId = "historical-muhammed-id", Name = Muhammed, PinCode = "1357", IsActive = false }
        };
        string savedBefore = JsonSerializer.Serialize(saved);
        var restarted = EmployeeRosterInitialization.Initialize(saved, defaults);
        Check(ReferenceEquals(saved, restarted), "nonempty roster is not replaced");
        Check(JsonSerializer.Serialize(restarted) == savedBefore, "rename preserves all stored fields");
        Check(!restarted.Any(item => item.Name == Adilet), "renamed default is not recreated");
        Check(restarted[0].EmployeeId == "original-adilet-id", "rename preserves permanent ID");
        Check(restarted[0].PinCode == "2468", "rename preserves saved credentials");
        Check(!restarted[1].IsActive, "restart does not reactivate disabled employees");
        for (int i = 0; i < 20; i++)
        {
            saved = JsonSerializer.Deserialize<List<Employee>>(JsonSerializer.Serialize(restarted))!;
            restarted = EmployeeRosterInitialization.Initialize(saved, defaults);
            Check(JsonSerializer.Serialize(restarted) == savedBefore, "durable repeated restart " + i);
        }

        foreach (bool active in new[] { false, true })
        {
            var partial = new List<Employee>
            {
                new() { EmployeeId = "custom-id", Name = "Custom", PinCode = "3579", IsActive = active }
            };
            string before = JsonSerializer.Serialize(partial);
            Check(JsonSerializer.Serialize(EmployeeRosterInitialization.Initialize(partial, defaults)) == before,
                "partial/custom roster stays authoritative, active=" + active);
        }
        var oldDisabled = new List<Employee>
        {
            new() { EmployeeId = "disabled-id", Name = Adilet, PinCode = "6543", IsActive = false }
        };
        Check(JsonSerializer.Serialize(EmployeeRosterInitialization.Initialize(oldDisabled, defaults)) ==
            JsonSerializer.Serialize(oldDisabled), "disabled defaults are neither replaced nor reactivated");
        var existingDuplicate = JsonSerializer.Deserialize<List<Employee>>(savedBefore)!;
        existingDuplicate.Add(new Employee { EmployeeId = "respawned-id", Name = Adilet, IsActive = false });
        string duplicateBefore = JsonSerializer.Serialize(existingDuplicate);
        Check(JsonSerializer.Serialize(EmployeeRosterInitialization.Initialize(existingDuplicate, defaults)) == duplicateBefore,
            "prevention does not silently merge or delete already existing profiles");

        // Inspect the production startup wiring without initializing its AppData-backed services.
        var startupCalls = CalledMethods(typeof(EmployeeService).TypeInitializer!).ToArray();
        Check(startupCalls.Any(method => method.DeclaringType == typeof(EmployeeRosterInitialization)),
            "EmployeeService startup uses the tested initialization policy");
        Check(!startupCalls.Any(method => method.Name == "EnsureDefaultEmployeesExist"),
            "EmployeeService startup cannot seed missing default names");
        var repairCalls = typeof(KnownDataRepairService)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .SelectMany(CalledMethods).ToArray();
        Check(!repairCalls.Any(method => method.DeclaringType == typeof(EmployeeReferenceRenameService)),
            "startup repairs cannot globally rename references by a reused employee name");
        Check(!repairCalls.Any(method => method.Name == "ApplyEmployeeRenameRepair"),
            "retired employee-name migration cannot be invoked on restart");
        Check(repairCalls.Any(method => method.Name == "ApplySalikhovAccumulatedCashlessRepair") &&
              repairCalls.Any(method => method.Name == "ApplySalikhovLateFinalizationRepair"),
            "unrelated Salikh cash repairs remain wired");
        Check(repairCalls.Any(method => method.Name == "ApplyTelecomSalaryRepair") &&
              repairCalls.Any(method => method.Name == "TryRepairKnownCashlessLoan"),
            "unrelated Telecom repairs remain wired");

        if (failures.Count != 0)
            throw new InvalidOperationException("Employee identity startup: " + string.Join("; ", failures));
        Console.WriteLine($"Employee identity startup: {checks} checks passed (pure data and IL metadata only).");
    }

    internal static IEnumerable<MethodBase> CalledMethods(MethodBase method)
    {
        byte[] il = method.GetMethodBody()?.GetILAsByteArray() ?? Array.Empty<byte>();
        int offset = 0;
        while (offset < il.Length)
        {
            short value = il[offset++];
            if (value == 0xfe) value = (short)(0xfe00 | il[offset++]);
            OpCode opcode = Opcodes[value];
            if (opcode.OperandType == OperandType.InlineMethod)
            {
                MethodBase? called = method.Module.ResolveMethod(BitConverter.ToInt32(il, offset));
                if (called != null) yield return called;
            }
            int size = opcode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI or OperandType.ShortInlineR or OperandType.InlineBrTarget or
                    OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineSig or
                    OperandType.InlineString or OperandType.InlineTok or OperandType.InlineType => 4,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                _ => throw new InvalidOperationException("Unsupported IL operand " + opcode.OperandType)
            };
            offset += size;
            if (offset > il.Length) throw new InvalidOperationException("Invalid IL operand boundary");
        }
    }
}
