using System.Diagnostics;
using System.Text;

namespace OtoMix.Services;

public sealed class RecorderService : IAsyncDisposable
{
    private static readonly TimeSpan StartupProbe = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly string _ffmpegPath;
    private Process? _process;
    private readonly Queue<string> _stderrTail = new();
    private bool _stopRequested;

    /// <param name="ffmpegPath">ffmpeg実行ファイル。stdinは音声データ(f32le)用なので 'q' は送らず、停止時はstdinを閉じてEOFで終わらせる。</param>
    public RecorderService(string ffmpegPath = "ffmpeg") => _ffmpegPath = ffmpegPath;

    /// <summary>音声データの書き込み先（録音中のみ有効）。</summary>
    public Stream? InputStream => _process is { HasExited: false } p ? p.StandardInput.BaseStream : null;

    public bool IsRunning => _process is { HasExited: false };

    /// <summary>ffmpegが想定外に終了した時に発火する（Stop() 経由の終了では発火しない）。引数はstderr末尾の1行。</summary>
    public event Action<string>? Crashed;

    /// <summary>ffmpegを起動する。起動直後に即終了した場合（デバイス不正など）は例外にする。</summary>
    public async Task StartAsync(IReadOnlyList<string> args, CancellationToken ct = default)
    {
        if (_process != null) throw new InvalidOperationException("すでに録音中です");

        var outputPath = args[^1];
        var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var psi = new ProcessStartInfo(_ffmpegPath)
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        _stopRequested = false;
        _stderrTail.Clear();
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        // stderr/stdout は常に読み続ける（詰まるとffmpegが止まる）
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (_stderrTail) { _stderrTail.Enqueue(e.Data); if (_stderrTail.Count > 20) _stderrTail.Dequeue(); } };
        p.OutputDataReceived += (_, _) => { };
        p.Exited += (_, _) => { if (!_stopRequested) Crashed?.Invoke(LastErrorLine()); };

        try { p.Start(); }
        catch (Exception ex) { p.Dispose(); throw new InvalidOperationException($"ffmpeg を起動できません: {ex.Message}", ex); }
        p.BeginErrorReadLine();
        p.BeginOutputReadLine();
        _process = p;

        try { await Task.Delay(StartupProbe, ct); }
        catch { await StopAsync(); throw; }
        if (p.HasExited)
        {
            var msg = LastErrorLine();
            _stopRequested = true;
            Cleanup();
            throw new InvalidOperationException($"録音を開始できません: {msg}");
        }
    }

    /// <summary>stdinを閉じてffmpegを正常終了させる（エンコードの仕上げを待つ）。5秒以内に終わらなければKill。</summary>
    public async Task StopAsync()
    {
        var p = _process;
        if (p == null) return;
        _stopRequested = true;
        try
        {
            if (!p.HasExited)
            {
                try
                {
                    p.StandardInput.Close();
                }
                catch (IOException) { /* すでに終了 */ }

                using var cts = new CancellationTokenSource(StopTimeout);
                try { await p.WaitForExitAsync(cts.Token); }
                catch (OperationCanceledException) { p.Kill(entireProcessTree: true); await p.WaitForExitAsync(); }
            }
        }
        finally { Cleanup(); }
    }

    private string LastErrorLine()
    {
        lock (_stderrTail) return _stderrTail.LastOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "ffmpegが終了しました";
    }

    private void Cleanup()
    {
        _process?.Dispose();
        _process = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
