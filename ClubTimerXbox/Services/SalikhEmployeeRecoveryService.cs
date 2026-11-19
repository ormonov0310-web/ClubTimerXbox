using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClubTimerXbox.Services;

internal static class SalikhEmployeeRecoveryService
{
    internal const string RecoveryDirectory = "repairs/salikh-employee-20261118-v1";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static void ApplyBeforeStartup() => Apply(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClubTimerXbox"),
        HasOtherProcess);

    internal static bool Apply(string root, Func<bool> hasOtherProcess, Action<int>? afterReplacement = null)
    {
        string identityPath = Path.Combine(root, "pc_identity.json");
        string directory = Path.Combine(root, RecoveryDirectory);
        string journalPath = Path.Combine(directory, "journal.json");
        JsonObject? identity = TryReadIdentity(identityPath) ?? TryReadIdentity(Path.Combine(root, "pc_identity.backup.json"));
        bool applies = identity != null && SalikhEmployeeRecoveryPlan.Text(identity, "ClubId") == "club_2" &&
            SalikhEmployeeRecoveryPlan.Text(identity, "InstallationId") == SalikhEmployeeRecoveryPlan.Installation;
        if (!applies && !File.Exists(journalPath)) return false;
        RecoveryJournal? journal = File.Exists(journalPath)
            ? JsonSerializer.Deserialize<RecoveryJournal>(File.ReadAllBytes(journalPath))
                ?? throw new InvalidDataException("Recovery journal is empty.")
            : null;
        if (journal != null)
        {
            Validate(journal);
            if (journal.State == "Committed") return false;
        }
        if (!applies) throw new InvalidDataException("Prepared employee recovery no longer matches this installation.");
        if (hasOtherProcess())
            throw new InvalidOperationException("Recovery requires the other ClubTimer process to be closed.");

        Dictionary<string, byte[]>? originals = null;
        Dictionary<string, byte[]>? outputs = null;
        if (journal == null)
        {
            originals = SalikhEmployeeRecoveryPlan.Files.ToDictionary(name => name,
                name => File.ReadAllBytes(Path.Combine(root, name)), StringComparer.Ordinal);
            var input = originals.ToDictionary(pair => pair.Key,
                pair => JsonNode.Parse(pair.Value) ?? throw new InvalidDataException(pair.Key), StringComparer.Ordinal);
            var plan = SalikhEmployeeRecoveryPlan.Build(input);
            outputs = SalikhEmployeeRecoveryPlan.Files.Take(7).ToDictionary(name => name,
                name => plan.TryGetValue(name, out JsonNode? changed)
                    ? JsonSerializer.SerializeToUtf8Bytes(changed, JsonOptions) : originals[name], StringComparer.Ordinal);
            // Keep the stores' fallback copies on the same repaired generation.
            foreach (string name in plan.Keys)
            {
                string backup = name + ".bak";
                if (!File.Exists(Path.Combine(root, backup))) continue;
                originals.Add(backup, File.ReadAllBytes(Path.Combine(root, backup)));
                outputs.Add(backup, outputs[name]);
            }
            journal = new RecoveryJournal
            {
                State = "Prepared",
                Files = outputs.Select(pair => new RecoveryFile
                {
                    Name = pair.Key, BeforeHash = Hash(originals[pair.Key]), AfterHash = Hash(pair.Value)
                }).ToList(),
                Proofs = originals.Where(pair => !outputs.ContainsKey(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => Hash(pair.Value), StringComparer.Ordinal)
            };
        }

        Directory.CreateDirectory(directory);
        using var exclusive = new FileStream(Path.Combine(directory, "repair.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        if (!File.Exists(journalPath))
        {
            foreach (var file in journal.Files)
            {
                WriteDurable(Path.Combine(directory, file.Name + ".original"), originals![file.Name]);
                WriteDurable(Path.Combine(directory, file.Name + ".staged"), outputs![file.Name]);
            }
            VerifyProofs(root, journal);
            foreach (var file in journal.Files)
                RequireHash(Path.Combine(root, file.Name), file.BeforeHash);
            WriteAtomic(journalPath, JsonSerializer.SerializeToUtf8Bytes(journal, JsonOptions));
        }
        else
        {
            // A second launch must not use a plan prepared before another launcher acquired the lock.
            journal = JsonSerializer.Deserialize<RecoveryJournal>(File.ReadAllBytes(journalPath))
                ?? throw new InvalidDataException("Recovery journal is empty.");
            Validate(journal);
            if (journal.State == "Committed") return false;
        }
        VerifyProofs(root, journal);
        // Prevalidate every file before resuming a partially committed generation.
        foreach (var file in journal.Files)
        {
            RequireHash(Path.Combine(directory, file.Name + ".original"), file.BeforeHash);
            RequireHash(Path.Combine(directory, file.Name + ".staged"), file.AfterHash);
            string hash = Hash(File.ReadAllBytes(Path.Combine(root, file.Name)));
            if (hash != file.BeforeHash && hash != file.AfterHash)
                throw new InvalidDataException("Recovery source changed: " + file.Name);
        }
        int replaced = 0;
        foreach (var file in journal.Files)
        {
            string destination = Path.Combine(root, file.Name);
            if (Hash(File.ReadAllBytes(destination)) == file.AfterHash) continue;
            RequireHash(destination, file.BeforeHash);
            WriteAtomic(destination, File.ReadAllBytes(Path.Combine(directory, file.Name + ".staged")));
            afterReplacement?.Invoke(++replaced);
        }
        journal.State = "Committed";
        WriteAtomic(journalPath, JsonSerializer.SerializeToUtf8Bytes(journal, JsonOptions));
        return true;
    }

    private static void VerifyProofs(string root, RecoveryJournal journal)
    {
        foreach (var proof in journal.Proofs) RequireHash(Path.Combine(root, proof.Key), proof.Value);
    }
    private static void Validate(RecoveryJournal journal)
    {
        var allowed = SalikhEmployeeRecoveryPlan.Files.Take(7).ToHashSet(StringComparer.Ordinal);
        allowed.UnionWith(allowed.ToArray().Select(name => name + ".bak"));
        if (journal.Version != 1 || journal.InstallationId != SalikhEmployeeRecoveryPlan.Installation ||
            journal.State is not ("Prepared" or "Committed") ||
            journal.Files.Select(file => file.Name).Distinct(StringComparer.Ordinal).Count() != journal.Files.Count ||
            journal.Files.Any(file => !allowed.Contains(file.Name)) ||
            journal.Proofs.Keys.Any(name => !SalikhEmployeeRecoveryPlan.Files.Contains(name, StringComparer.Ordinal)) ||
            (journal.Files.Count == 0 ? journal.State != "Committed" :
                SalikhEmployeeRecoveryPlan.Files.Take(7).Any(name => !journal.Files.Any(file => file.Name == name))) ||
            SalikhEmployeeRecoveryPlan.Files.Where(name => !journal.Files.Any(file => file.Name == name))
                .Any(name => !journal.Proofs.ContainsKey(name)) ||
            journal.Files.Any(file => !ValidHash(file.BeforeHash) || !ValidHash(file.AfterHash)) ||
            journal.Proofs.Values.Any(value => !ValidHash(value)))
            throw new InvalidDataException("Invalid employee recovery journal.");
    }
    private static bool ValidHash(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
    private static JsonObject? TryReadIdentity(string path)
    {
        try
        {
            if (!File.Exists(path) || JsonNode.Parse(File.ReadAllBytes(path)) is not JsonObject identity ||
                identity["ClubId"] is not JsonValue club || !club.TryGetValue<string>(out _) ||
                identity["InstallationId"] is not JsonValue installation || !installation.TryGetValue<string>(out _))
                return null;
            return identity;
        }
        catch (Exception ex) when (ex is JsonException or IOException) { return null; }
    }
    private static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value));
    private static void RequireHash(string path, string expected)
    {
        if (Hash(File.ReadAllBytes(path)) != expected)
            throw new InvalidDataException("Recovery file changed: " + Path.GetFileName(path));
    }
    private static void WriteAtomic(string path, byte[] value)
    {
        string temporary = path + ".employee-recovery.tmp";
        WriteDurable(temporary, value);
        File.Move(temporary, path, true);
    }
    private static void WriteDurable(string path, byte[] value)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
            4096, FileOptions.WriteThrough);
        stream.Write(value);
        stream.Flush(true);
    }
    private static bool HasOtherProcess()
    {
        int ownId = Environment.ProcessId;
        foreach (Process process in Process.GetProcessesByName("ClubTimerXbox"))
        {
            using (process)
                if (process.Id != ownId) return true;
        }
        return false;
    }

    internal sealed class RecoveryJournal
    {
        public int Version { get; set; } = 1;
        public string InstallationId { get; set; } = SalikhEmployeeRecoveryPlan.Installation;
        public string State { get; set; } = "";
        public List<RecoveryFile> Files { get; set; } = new();
        public Dictionary<string, string> Proofs { get; set; } = new(StringComparer.Ordinal);
    }
    internal sealed class RecoveryFile
    {
        public string Name { get; set; } = "";
        public string BeforeHash { get; set; } = "";
        public string AfterHash { get; set; } = "";
    }
}
