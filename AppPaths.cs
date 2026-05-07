using System;
using System.IO;

namespace DoneToday;

/// <summary>
/// Resolves the per-user data directory used by every file-based store
/// (Settings, CompletedLog, MainWindow's todos.json).
/// </summary>
public static class AppPaths
{
    private static readonly string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public static readonly string NewDir = Path.Combine(AppData, "DoneToday");

    private static bool _ensured;
    private static readonly object _lock = new();

    /// <summary>The path to use for reading/writing all app data.</summary>
    public static string DataDir
    {
        get
        {
            EnsureExists();
            return NewDir;
        }
    }

    private static void EnsureExists()
    {
        if (_ensured) return;
        lock (_lock)
        {
            if (_ensured) return;
            try { Directory.CreateDirectory(NewDir); } catch { /* best-effort */ }
            _ensured = true;
        }
    }
}
