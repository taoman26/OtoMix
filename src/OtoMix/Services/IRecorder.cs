using OtoMix.Models;

namespace OtoMix.Services;

public interface IRecorder : IAsyncDisposable
{
    bool IsRunning { get; }

    /// <summary>想定外に録音が止まった時に発火する（StopAsync 経由では発火しない）。引数は理由。</summary>
    event Action<string>? Crashed;

    /// <summary>入力を無音にして録音する。録音前・録音中のどちらでも呼べる（長さは変わらず、その入力だけ無音になる）。</summary>
    void SetMuted(bool microphone, bool muted);

    Task StartAsync(AudioDevice microphone, AudioDevice loopback, string outputPath, CancellationToken ct = default);
    Task StopAsync();
}

public static class RecorderFactory
{
    public static IRecorder Create() =>
        OperatingSystem.IsWindows()
            ? new MixingRecorder(d => new WasapiCaptureSource(d))
            : new MixingRecorder(d => new ParecCaptureSource(d));
}
