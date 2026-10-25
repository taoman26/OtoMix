using System.Text.Json;
using OtoMix.Models;

namespace OtoMix.Services;

public static class PactlParser
{
    private const string MonitorSuffix = ".monitor";

    /// <summary>`pactl -f json list sources` の出力を分類する。.monitor 終端はループバック、それ以外はマイク。</summary>
    public static DeviceList ParseJson(string json)
    {
        var mics = new List<AudioDevice>();
        var loops = new List<AudioDevice>();
        using var doc = JsonDocument.Parse(json);
        foreach (var src in doc.RootElement.EnumerateArray())
        {
            var name = src.GetProperty("name").GetString();
            if (string.IsNullOrEmpty(name)) continue;
            var desc = src.TryGetProperty("description", out var d) ? d.GetString() : null;
            Add(name, IsUsableDescription(desc) ? desc! : name, mics, loops);
        }
        return new DeviceList(mics, loops);
    }

    // PulseAudio 15.99 のJSON出力は説明文に非ASCII(日本語など)があると文字列 "(null)" を出す。
    private static bool IsUsableDescription(string? d) => !string.IsNullOrWhiteSpace(d) && d != "(null)";

    /// <summary>古いpactl用フォールバック: `pactl list short sources`（index\tname\t...）。</summary>
    public static DeviceList ParseShort(string text)
    {
        var mics = new List<AudioDevice>();
        var loops = new List<AudioDevice>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var cols = line.Split('\t');
            if (cols.Length < 2) continue;
            Add(cols[1].Trim(), cols[1].Trim(), mics, loops);
        }
        return new DeviceList(mics, loops);
    }

    private static void Add(string name, string display, List<AudioDevice> mics, List<AudioDevice> loops)
    {
        if (name.EndsWith(MonitorSuffix, StringComparison.Ordinal))
            loops.Add(new AudioDevice(name, display, DeviceKind.Loopback));
        else
            mics.Add(new AudioDevice(name, display, DeviceKind.Microphone));
    }
}
