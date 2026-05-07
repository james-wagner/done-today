using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace DoneToday;

public class UpdateInfo
{
    public string Version { get; set; } = "";
    public string Exe { get; set; } = "";          // absolute URL/path, or relative to the source
    public string? Notes { get; set; }
}

public static class UpdateChecker
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <summary>Checks the configured source for a newer version. Returns the info if newer, null otherwise.</summary>
    public static async Task<UpdateInfo?> CheckAsync(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        try
        {
            string json = await ReadManifestAsync(source);
            if (string.IsNullOrWhiteSpace(json)) return null;

            var info = JsonSerializer.Deserialize<UpdateInfo>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (info == null || string.IsNullOrWhiteSpace(info.Version)) return null;

            if (!TryParseVersion(info.Version, out var newVer)) return null;
            return newVer > NormalizeCurrent() ? info : null;
        }
        catch
        {
            return null;
        }
    }

    private static Version NormalizeCurrent()
    {
        var v = CurrentVersion;
        // Compare on Major.Minor.Build (Revision is irrelevant for our SemVer-ish bumps)
        return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
    }

    private static bool TryParseVersion(string s, out Version v)
    {
        if (Version.TryParse(s, out var parsed))
        {
            v = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build));
            return true;
        }
        v = new Version();
        return false;
    }

    private static async Task<string> ReadManifestAsync(string source)
    {
        if (IsUrl(source))
        {
            // Treat URL as the manifest itself if it ends with .json, otherwise append /latest.json
            var url = source.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? source
                : source.TrimEnd('/') + "/latest.json";
            return await Http.GetStringAsync(url);
        }

        if (Directory.Exists(source))
        {
            var jsonFile = Path.Combine(source, "latest.json");
            return File.Exists(jsonFile) ? await File.ReadAllTextAsync(jsonFile) : "";
        }

        if (File.Exists(source))
        {
            return await File.ReadAllTextAsync(source);
        }

        return "";
    }

    private static bool IsUrl(string s) =>
        s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>Downloads the new exe to a temp file and returns its path.</summary>
    public static async Task<string?> DownloadAsync(UpdateInfo info, string source)
    {
        try
        {
            string exeRef = info.Exe;
            if (string.IsNullOrWhiteSpace(exeRef)) return null;

            // Resolve relative refs against the source's directory
            if (!IsUrl(exeRef) && !Path.IsPathRooted(exeRef))
            {
                if (IsUrl(source))
                {
                    var baseUrl = source.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                        ? source.Substring(0, source.LastIndexOf('/') + 1)
                        : source.TrimEnd('/') + "/";
                    exeRef = baseUrl + exeRef;
                }
                else
                {
                    var baseDir = Directory.Exists(source) ? source : Path.GetDirectoryName(source) ?? "";
                    exeRef = Path.Combine(baseDir, exeRef);
                }
            }

            var temp = Path.Combine(Path.GetTempPath(), $"DoneToday-{info.Version}.exe");

            if (IsUrl(exeRef))
            {
                using var stream = await Http.GetStreamAsync(exeRef);
                using var file = File.Create(temp);
                await stream.CopyToAsync(file);
            }
            else
            {
                File.Copy(exeRef, temp, true);
            }

            return temp;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Replaces the running exe with the new one and relaunches it. Shuts down the current app.</summary>
    public static void InstallAndRestart(string newExePath, string targetExePath)
    {
        // Spawn a tiny PowerShell that waits for us to exit, replaces the file, and relaunches.
        var logPath = Path.Combine(Path.GetTempPath(), "DoneToday-Update.log");
        var script = $@"
$ErrorActionPreference = 'Stop'
try {{
    Start-Sleep -Milliseconds 1200
    Copy-Item -LiteralPath '{newExePath.Replace("'", "''")}' -Destination '{targetExePath.Replace("'", "''")}' -Force
    Start-Process -FilePath '{targetExePath.Replace("'", "''")}'
}} catch {{
    Add-Content -Path '{logPath.Replace("'", "''")}' -Value (Get-Date).ToString('s')
    Add-Content -Path '{logPath.Replace("'", "''")}' -Value $_.Exception.ToString()
}}
";
        var scriptPath = Path.Combine(Path.GetTempPath(), "DoneToday-Updater.ps1");
        File.WriteAllText(scriptPath, script);

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });

        Application.Current.Shutdown();
    }
}
