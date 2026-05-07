using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DoneToday;

public class Settings
{
    private static string Dir => AppPaths.DataDir;
    private static string Path_ => Path.Combine(Dir, "settings.json");

    // 0x2 = MOD_CONTROL, 0x1 = MOD_ALT  →  Ctrl+Alt
    public uint HotkeyModifiers { get; set; } = 0x2 | 0x1;
    // 0x54 = 'T'
    public uint HotkeyVirtualKey { get; set; } = 0x54;

    public double FontSize { get; set; } = 16;

    /// <summary>URL (http(s)) or local folder/file path holding latest.json (with version + exe).</summary>
    public string UpdateSource { get; set; } = "";

    // Saved window geometry. Null on first run → fall back to default position.
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }

    [JsonIgnore]
    public bool HotkeyEnabled => HotkeyVirtualKey != 0;

    public static Settings Load()
    {
        try
        {
            if (File.Exists(Path_))
            {
                var json = File.ReadAllText(Path_);
                return JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
            }
        }
        catch { }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(Path_, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public string DescribeHotkey()
    {
        if (!HotkeyEnabled) return "(none)";
        return HotkeyFormatter.Format(HotkeyModifiers, HotkeyVirtualKey);
    }
}

public static class HotkeyFormatter
{
    // Win32 modifier flags
    public const uint MOD_ALT = 0x1;
    public const uint MOD_CONTROL = 0x2;
    public const uint MOD_SHIFT = 0x4;
    public const uint MOD_WIN = 0x8;
    public const uint MOD_NOREPEAT = 0x4000;

    public static string Format(uint mods, uint vk)
    {
        var parts = new System.Collections.Generic.List<string>();
        if ((mods & MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((mods & MOD_ALT) != 0) parts.Add("Alt");
        if ((mods & MOD_SHIFT) != 0) parts.Add("Shift");
        if ((mods & MOD_WIN) != 0) parts.Add("Win");
        if (vk != 0) parts.Add(VkName(vk));
        return string.Join(" + ", parts);
    }

    private static string VkName(uint vk)
    {
        // Letters / digits
        if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
        if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
        // Function keys
        if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);
        // Common named
        return vk switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x13 => "Pause",
            0x14 => "CapsLock",
            0x1B => "Esc",
            0x20 => "Space",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2C => "PrintScreen",
            0x2D => "Insert",
            0x2E => "Delete",
            0xBA => ";",
            0xBB => "=",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xDE => "'",
            _ => "VK(0x" + vk.ToString("X2") + ")"
        };
    }
}
