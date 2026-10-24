using System.Diagnostics;
using System.Text;
using NAudio.Wave;
using OtoMix.Models;

namespace OtoMix.Services;

/// <summary>
/// Linux: `parec` で PulseAudio/PipeWire のソース（マイク or *.monitor）を 48kHz/stereo/float32 で受け取る。
/// サンプルレートやチャンネル変換は Pulse 側が行う。
/// </summary>
public sealed class ParecCaptureSource : ICaptureSource
{
    private readonly AudioDevice _device;
    private readonly string _parecPath;
    private Process? _process;
    private Task? _reader;
    private volatile bool _stopRequested;
    private readonly StringBuilder _stderr = new();

    public ParecCaptureSource(AudioDevice device, string parecPath = "parec")
    {
        _device = device;
        _parecPath = parecPath;
    }

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    public event Action<byte[], int>? DataAvailable;
    public event Action<Exception?>? Stopped;

    public void Start()
    {
        var psi = new ProcessStartInfo(_parecPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true, // 親が死んだら parec も EOF で気づけるよう閉じずに保持
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in new[]
        {
            "--device=" + _device.Id, "--format=float32le", "--rate=48000", "--channels=2",
            "--raw", "--latency-msec=20", "--client-name=OtoMix", "--stream-name=OtoMix capture",
        }) psi.ArgumentList.Add(a);

        try { _process = Process.Start(psi) ?? throw new InvalidOperationException("parec を起動できません"); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("parec を起動できません（pulseaudio-utils が必要です）", ex);
        }
        _process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (_stderr) _stderr.AppendLine(e.Data); };
        _process.BeginErrorReadLine();
        var p = _process;
        _reader = Task.Run(() => ReadLoop(p));
    }

    private void ReadLoop(Process p)
    {
        var buf = new byte[4096]; // float32 stereo で約10.7ms
        try
        {
            var s = p.StandardOutput.BaseStream;
            int n;
            while ((n = s.Read(buf, 0, buf.Length)) > 0) DataAvailable?.Invoke(buf, n);
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }

        if (_stopRequested) { Stopped?.Invoke(null); return; }
        p.WaitForExit(500);
        string err;
        lock (_stderr) err = _stderr.ToString().Trim().Split('\n').LastOrDefault()?.Trim() ?? "";
        Stopped?.Invoke(new InvalidOperationException(string.IsNullOrEmpty(err) ? $"parec が終了しました ({_device.DisplayName})" : err));
    }

    public void Stop()
    {
        _stopRequested = true;
        try { if (_process is { HasExited: false }) _process.Kill(); } catch (InvalidOperationException) { }
        _reader?.Wait(1000);
    }

    public void Dispose()
    {
        Stop();
        _process?.Dispose();
        _process = null;
    }
}
