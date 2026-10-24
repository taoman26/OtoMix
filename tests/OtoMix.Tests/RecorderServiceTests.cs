using System.Diagnostics;
using OtoMix.Services;

namespace OtoMix.Tests;

public class RecorderServiceTests
{
    private static bool HasPulse => OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("PULSE_SERVER") is not null;

    [Fact]
    public async Task InvalidDevice_ThrowsWithFfmpegMessage()
    {
        if (!HasPulse) return;
        var path = Path.Combine(Path.GetTempPath(), "otomix_bad.mp3");
        await using var rec = new RecorderService();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => rec.StartAsync(new[]
        {
            "-hide_banner", "-y", "-f", "pulse", "-i", "no-such-device", "-f", "pulse", "-i", "no-such-2",
            "-filter_complex", "amix=inputs=2", path,
        }));
        Console.WriteLine("error: " + ex.Message);
        Assert.False(rec.IsRunning);
    }

    [Fact]
    public async Task Unresponsive_Process_IsKilledAfterTimeout()
    {
        if (!OperatingSystem.IsLinux()) return;
        // stdinを無視して居座るダミー（ffmpegが固まった状況）
        var script = Path.Combine(Path.GetTempPath(), $"otomix_hang_{Guid.NewGuid():N}.sh");
        await File.WriteAllTextAsync(script, "#!/bin/sh\nexec sleep 60\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        await using var rec = new RecorderService(script);
        await rec.StartAsync(new[] { "x", "/tmp/otomix_hang.mp3" });
        var sw = Stopwatch.StartNew();
        await rec.StopAsync();
        Console.WriteLine($"kill path took {sw.ElapsedMilliseconds}ms");
        Assert.False(rec.IsRunning);
        Assert.InRange(sw.ElapsedMilliseconds, 4500, 8000);
        File.Delete(script);
    }

    [Fact]
    public async Task UnexpectedExit_RaisesCrashed()
    {
        if (!OperatingSystem.IsLinux()) return;
        var script = Path.Combine(Path.GetTempPath(), $"otomix_die_{Guid.NewGuid():N}.sh");
        await File.WriteAllTextAsync(script, "#!/bin/sh\nsleep 1.5\necho 'device lost' >&2\nexit 3\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        await using var rec = new RecorderService(script);
        var tcs = new TaskCompletionSource<string>();
        rec.Crashed += m => tcs.TrySetResult(m);
        await rec.StartAsync(new[] { "x", "/tmp/otomix_die.mp3" });
        var msg = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("device lost", msg);
        File.Delete(script);
    }
}
