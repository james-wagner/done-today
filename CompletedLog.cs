using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace DoneToday;

public class CompletedEntry
{
    public Guid ItemId { get; set; }
    public string Text { get; set; } = "";
    public string Details { get; set; } = "";
    public DateTime CompletedAt { get; set; }
    public TimeSpan? TimeToComplete { get; set; }
    public Guid? ParentItemId { get; set; }
    public string? ParentText { get; set; }
}

public static class DurationFormatter
{
    public static string Format(TimeSpan d)
    {
        int h = (int)Math.Floor(d.TotalHours);
        int m = d.Minutes;
        if (h > 0 && m > 0) return $"{h}h {m}m";
        if (h > 0) return $"{h}h";
        return $"{m}m";
    }
}

public static class CompletedLog
{
    private static string Dir => AppPaths.DataDir;
    private static string Path_ => Path.Combine(Dir, "completed.log.json");

    /// <summary>Absolute path to the JSON log file (for handing to other tools to read).</summary>
    public static string FilePath => Path_;

    public static List<CompletedEntry> LoadAll()
    {
        try
        {
            if (!File.Exists(Path_)) return new List<CompletedEntry>();
            var json = File.ReadAllText(Path_);
            return JsonSerializer.Deserialize<List<CompletedEntry>>(json) ?? new List<CompletedEntry>();
        }
        catch
        {
            return new List<CompletedEntry>();
        }
    }

    public static void Append(CompletedEntry entry)
    {
        var all = LoadAll();
        all.Add(entry);
        SaveAll(all);
    }

    /// <summary>Updates the most-recent entry for the given item id.</summary>
    public static void UpdateLatestByItemId(Guid itemId, DateTime newCompletedAt, string newText, string newDetails, TimeSpan? newTimeToComplete)
    {
        var all = LoadAll();
        for (int i = all.Count - 1; i >= 0; i--)
        {
            if (all[i].ItemId == itemId)
            {
                all[i].CompletedAt = newCompletedAt;
                all[i].Text = newText;
                all[i].Details = newDetails;
                all[i].TimeToComplete = newTimeToComplete;
                SaveAll(all);
                return;
            }
        }
    }

    /// <summary>Removes the most-recent entry matching the given item id.</summary>
    public static void RemoveLatestByItemId(Guid itemId)
    {
        var all = LoadAll();
        for (int i = all.Count - 1; i >= 0; i--)
        {
            if (all[i].ItemId == itemId)
            {
                all.RemoveAt(i);
                SaveAll(all);
                return;
            }
        }
    }

    public static void Clear() => SaveAll(new List<CompletedEntry>());

    private static void SaveAll(List<CompletedEntry> all)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(Path_, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    /// <summary>
    /// Format for pasting into Azure DevOps comments / work items.
    /// Groups by date (newest first) with bullet items underneath.
    /// </summary>
    public static string FormatForAdo(IEnumerable<CompletedEntry> entries)
    {
        var sb = new StringBuilder();
        var groups = entries
            .OrderByDescending(e => e.CompletedAt)
            .GroupBy(e => e.CompletedAt.Date)
            .OrderByDescending(g => g.Key);

        bool firstGroup = true;
        foreach (var g in groups)
        {
            if (!firstGroup) sb.AppendLine();
            firstGroup = false;
            sb.AppendLine($"[{g.Key:yyyy-MM-dd}]");

            var dayList = g.OrderBy(e => e.CompletedAt).ToList();
            var emitted = new HashSet<CompletedEntry>(ReferenceEqualityComparer.Instance);

            // Top-level entries first, with their same-day children nested below
            foreach (var top in dayList.Where(e => !e.ParentItemId.HasValue))
            {
                sb.AppendLine($"- {top.Text}");
                AppendNotes(sb, top.Details, top.TimeToComplete, indent: 4);
                emitted.Add(top);

                foreach (var child in dayList.Where(c => c.ParentItemId == top.ItemId).OrderBy(c => c.CompletedAt))
                {
                    sb.AppendLine($"    - {top.Text}: {child.Text}");
                    AppendNotes(sb, child.Details, child.TimeToComplete, indent: 8);
                    emitted.Add(child);
                }
            }

            // Orphaned subtasks (parent wasn't completed on the same day)
            foreach (var k in dayList.Where(e => e.ParentItemId.HasValue && !emitted.Contains(e)))
            {
                var label = string.IsNullOrWhiteSpace(k.ParentText) ? "subtask" : k.ParentText;
                sb.AppendLine($"- {label}: {k.Text}");
                AppendNotes(sb, k.Details, k.TimeToComplete, indent: 4);
            }
        }
        return sb.ToString().TrimEnd();
    }

    private static void AppendNotes(StringBuilder sb, string details, TimeSpan? duration, int indent)
    {
        var pad = new string(' ', indent);
        if (duration is TimeSpan d && d > TimeSpan.Zero)
        {
            sb.AppendLine($"{pad}Took: {DurationFormatter.Format(d)}");
        }
        if (string.IsNullOrWhiteSpace(details)) return;
        foreach (var line in details.Split('\n'))
        {
            sb.AppendLine($"{pad}{line.TrimEnd('\r')}");
        }
    }
}
