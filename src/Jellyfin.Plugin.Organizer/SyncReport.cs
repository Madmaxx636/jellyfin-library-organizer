using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Jellyfin.Plugin.Organizer;

/// <summary>What the last sync did, kept in a small file next to the settings so the settings page can show it.</summary>
public class SyncReport
{
    public DateTime TimeUtc { get; set; }
    public double Seconds { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public int Skipped { get; set; }
    public int Removed { get; set; }
    public List<string> Lines { get; set; } = new();

    private static string? PathOrNull => Plugin.Instance is { } p ? Path.Combine(p.DataFolderPath, "last-sync.json") : null;

    public static SyncReport? Load()
    {
        try
        {
            return PathOrNull is { } path && File.Exists(path)
                ? JsonSerializer.Deserialize<SyncReport>(File.ReadAllText(path))
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Save()
    {
        try
        {
            if (PathOrNull is { } path)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(this));
            }
        }
        catch (Exception)
        {
            // The report is a convenience; never fail a sync over it.
        }
    }
}

/// <summary>One line of the "preview changes" report.</summary>
public record PlannedChange(string Name, string Kind, string Action, int Count, int Add, int Remove, string Note);
