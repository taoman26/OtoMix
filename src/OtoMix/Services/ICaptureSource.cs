using NAudio.Wave;

namespace OtoMix.Services;

/// <summary>1つの入力デバイスから生のPCMを取り出す。OSごとの実装（WASAPI / parec）を MixingRecorder から隠す。</summary>
public interface ICaptureSource : IDisposable
{
    WaveFormat WaveFormat { get; }

    /// <summary>PCMチャンクが届いた（呼び出しスレッドは実装依存）。</summary>
    event Action<byte[], int>? DataAvailable;

    /// <summary>取得が止まった。例外つきなら異常終了。</summary>
    event Action<Exception?>? Stopped;

    void Start();
    void Stop();
}
