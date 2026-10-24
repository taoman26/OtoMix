using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using OtoMix.Models;
using OtoMix.Services;

namespace OtoMix.Tests;

/// <summary>
/// Linux実機(PulseAudio)で、独立した仮想音源2つ（A=440Hz をマイク役、B=880Hz をスピーカー役）を実際に録音し、
/// ミュートが「いつ」効くかを測る。PULSE_SERVER があるLinuxでのみ動く。
/// </summary>
public class LinuxMixingTests : IAsyncLifetime
{
    private static bool Enabled => OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("PULSE_SERVER") is not null;
    private readonly List<string> _modules = new();
    private readonly List<Process> _tones = new();

    private static AudioDevice DevA => new("otomix_t_a.monitor", "A(mic role)", DeviceKind.Microphone);
    private static AudioDevice DevB => new("otomix_t_b.monitor", "B(speaker role)", DeviceKind.Loopback);

    public async Task InitializeAsync()
    {
        if (!Enabled) return;
        foreach (var n in new[] { "otomix_t_a", "otomix_t_b" })
            _modules.Add((await Run("pactl", "load-module", "module-null-sink", $"sink_name={n}")).Trim());
        // 各シンクへ別々の周波数を流し続ける（録音中ずっと鳴っている状態）
        _tones.Add(StartTone(440, "otomix_t_a"));
        _tones.Add(StartTone(880, "otomix_t_b"));
        await Task.Delay(500);
    }

    public async Task DisposeAsync()
    {
        foreach (var t in _tones) { try { t.Kill(); t.Dispose(); } catch { } }
        foreach (var m in _modules) await Run("pactl", "unload-module", m);
    }

    private static Process StartTone(int hz, string sink)
    {
        var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true, RedirectStandardInput = true };
        foreach (var a in new[] { "-hide_banner", "-loglevel", "error", "-re", "-f", "lavfi", "-i", $"sine=frequency={hz}", "-f", "pulse", "-device", sink, "OtoMix test tone" }) psi.ArgumentList.Add(a);
        return Process.Start(psi)!;
    }

    private static async Task<string> Run(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var o = await p.StandardOutput.ReadToEndAsync();
        var e = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return o + e;
    }

    /// <summary>指定周波数の成分だけを 50ms ごとのRMS(dB)で返す。</summary>
    private static async Task<List<double>> Levels(string path, int hz)
    {
        var o = await Run("ffmpeg", "-hide_banner", "-i", path, "-af",
            $"bandpass=f={hz}:width_type=h:w=40,asetnsamples=n=2400,astats=metadata=1:reset=1,ametadata=print:key=lavfi.astats.Overall.RMS_level:file=-",
            "-f", "null", "-");
        return Regex.Matches(o, @"RMS_level=(-?[\d.]+|-inf)")
            .Select(m => m.Groups[1].Value == "-inf" ? -99.0 : double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
    }

    [Fact]
    public async Task LiveMute_TakesEffectImmediately_AndOnlyOnThatInput()
    {
        if (!Enabled) return;
        var path = Path.Combine(Path.GetTempPath(), $"otomix_lx_{Guid.NewGuid():N}.wav");
        await using var rec = new MixingRecorder(d => new ParecCaptureSource(d));
        await rec.StartAsync(DevA, DevB, path);
        var clock = Stopwatch.StartNew();          // StartAsync完了 ≒ ポンプ開始 ≒ ファイル時刻0
        await Task.Delay(1500);
        var tMute = clock.Elapsed.TotalSeconds; rec.SetMuted(microphone: true, muted: true);
        await Task.Delay(1500);
        var tUnmute = clock.Elapsed.TotalSeconds; rec.SetMuted(microphone: true, muted: false);
        await Task.Delay(1500);
        await rec.StopAsync();

        var a = await Levels(path, 440);
        var b = await Levels(path, 880);
        const int Skip = 10; // parecの接続待ちで先頭約0.35sは無音（取得開始前）なので0.5s以降を見る
        int muteIdx = a.FindIndex(Skip, v => v < -45);
        int unmuteIdx = a.FindIndex(muteIdx, v => v > -30);
        double onset = muteIdx * 0.05, back = unmuteIdx * 0.05;
        Console.WriteLine($"mute sent {tMute:0.000}s -> silent from {onset:0.00}s (lag {(onset - tMute) * 1000:0}ms)");
        Console.WriteLine($"unmute sent {tUnmute:0.000}s -> audible again from {back:0.00}s (lag {(back - tUnmute) * 1000:0}ms)");
        Console.WriteLine($"B(880Hz) min level over file (0.5s以降) = {b.Skip(Skip).Take(b.Count - Skip - 5).Min():0}dB (must stay audible)");

        Assert.True(a[Skip] > -30, "ミュート前はAが入っている");
        Assert.InRange(onset - tMute, -0.1, 0.2);      // 約0.3〜0.5秒だったffmpeg方式より大幅に短い
        Assert.InRange(back - tUnmute, -0.1, 0.2);
        Assert.True(b.Skip(Skip).Take(b.Count - Skip - 5).Min() > -30, "Bはミュートの影響を受けず入り続ける");
        File.Delete(path);
    }

    [Fact]
    public async Task StartMuted_Loopback_IsSilentThroughout_MicStays()
    {
        if (!Enabled) return;
        var path = Path.Combine(Path.GetTempPath(), $"otomix_lx2_{Guid.NewGuid():N}.wav");
        await using var rec = new MixingRecorder(d => new ParecCaptureSource(d));
        rec.SetMuted(microphone: false, muted: true);
        await rec.StartAsync(DevA, DevB, path);
        await Task.Delay(2000);
        await rec.StopAsync();
        var a = await Levels(path, 440);
        var b = await Levels(path, 880);
        Console.WriteLine("A: " + string.Join(" ", a.Select(v => v.ToString("0"))));
        Console.WriteLine($"A min={a.Skip(10).Min():0}dB  B max={b.Max():0}dB");
        Assert.True(a.Skip(10).Take(a.Count - 15).Min() > -30);
        Assert.True(b.Max() < -45); // 440Hzのバンドパス漏れ(-50dB前後)以下
        File.Delete(path);
    }

    [Fact]
    public async Task InvalidSource_FailsToStart_WithParecMessage()
    {
        if (!Enabled) return;
        var path = Path.Combine(Path.GetTempPath(), $"otomix_lx3_{Guid.NewGuid():N}.mp3");
        await using var rec = new MixingRecorder(d => new ParecCaptureSource(d));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            rec.StartAsync(new AudioDevice("no-such-source", "x", DeviceKind.Microphone), DevB, path));
        Console.WriteLine("error: " + ex.Message);
        Assert.False(rec.IsRunning);
        Assert.False(File.Exists(path) && new FileInfo(path).Length > 1000);
    }
}
