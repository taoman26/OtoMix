using System.Diagnostics;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using OtoMix.Models;

namespace OtoMix.Services;

/// <summary>
/// OS共通の録音: マイクとシステム音声を ICaptureSource で別々に取得し、アプリ内で48kHz/stereo/floatにそろえて加算し、
/// f32le PCM を ffmpeg の stdin へ流してエンコードする（ミュートはその場で入力ごとに無音化するので即時に効く）。
/// システム音声は再生が無いとデータが来ないため、実時間クロックで一定量ずつ読み、欠けた分は無音で埋める。
/// </summary>
public sealed class MixingRecorder : IRecorder
{
    private const int SampleRate = 48000;
    private const int Channels = 2;
    private static readonly TimeSpan StartupProbe = TimeSpan.FromMilliseconds(400);

    private readonly string _ffmpegPath;
    private readonly Func<AudioDevice, ICaptureSource> _createSource;
    private RecorderService? _ffmpeg;
    private ICaptureSource? _mic, _loop;
    private Thread? _pump;
    private volatile bool _stopping;
    private volatile bool _micMuted, _loopMuted;
    private volatile string? _startupFailure;
    private bool _started;
    private int _crashReported;

    public MixingRecorder(Func<AudioDevice, ICaptureSource> createSource, string ffmpegPath = "ffmpeg")
    {
        _createSource = createSource;
        _ffmpegPath = ffmpegPath;
    }

    public bool IsRunning => _ffmpeg?.IsRunning == true && _pump is { IsAlive: true };
    public event Action<string>? Crashed;

    public void SetMuted(bool microphone, bool muted)
    {
        if (microphone) _micMuted = muted; else _loopMuted = muted;
    }

    public async Task StartAsync(AudioDevice microphone, AudioDevice loopback, string outputPath, CancellationToken ct = default)
    {
        if (_ffmpeg != null) throw new InvalidOperationException("すでに録音中です");
        _stopping = false;
        _started = false;
        _startupFailure = null;
        _crashReported = 0;

        _ffmpeg = new RecorderService(_ffmpegPath);
        _ffmpeg.Crashed += ReportCrash;
        try
        {
            // ffmpeg が起動できることを先に確かめる（stdin待ちで生きていればOK）
            await _ffmpeg.StartAsync(FfmpegCommandBuilder.BuildPipeEncoder(outputPath, SampleRate, Channels), ct);

            _mic = _createSource(microphone);
            _loop = _createSource(loopback);
            var mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels)) { ReadFully = true };
            mixer.AddMixerInput(new MuteSampleProvider(Feed(_mic, "マイク"), () => _micMuted));
            mixer.AddMixerInput(new MuteSampleProvider(Feed(_loop, "システム音声"), () => _loopMuted));

            _mic.Start();
            _loop.Start();

            // 存在しないデバイス等で取得プロセスがすぐ死ぬケースを開始エラーにする
            await Task.Delay(StartupProbe, ct);
            if (_startupFailure != null) throw new InvalidOperationException($"録音を開始できません: {_startupFailure}");
            _started = true;

            _pump = new Thread(() => Pump(mixer)) { IsBackground = true, Name = "OtoMix.Pump", Priority = ThreadPriority.AboveNormal };
            _pump.Start();
        }
        catch
        {
            await TearDownAsync();
            throw;
        }
    }

    public async Task StopAsync() => await TearDownAsync();

    private ISampleProvider Feed(ICaptureSource source, string label)
    {
        var buffer = new BufferedWaveProvider(source.WaveFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(2),
            ReadFully = true, // データが無い間は無音を返す
        };
        source.DataAvailable += (data, count) => buffer.AddSamples(data, 0, count);
        source.Stopped += ex =>
        {
            if (ex == null || _stopping) return;
            var msg = _started ? $"{label}の取得が中断されました: {ex.Message}" : ex.Message;
            if (_started) ReportCrash(msg); else _startupFailure = msg;
        };

        ISampleProvider sp = buffer.ToSampleProvider();
        if (sp.WaveFormat.SampleRate != SampleRate) sp = new WdlResamplingSampleProvider(sp, SampleRate);
        return new ToStereoSampleProvider(sp);
    }

    private void Pump(ISampleProvider mixer)
    {
        var sw = Stopwatch.StartNew();
        long framesWritten = 0;
        var floats = new float[SampleRate * Channels]; // 最大1秒分
        var bytes = new byte[floats.Length * sizeof(float)];
        try
        {
            while (!_stopping)
            {
                long due = (long)(sw.Elapsed.TotalSeconds * SampleRate) - framesWritten;
                if (due < 480) { Thread.Sleep(5); continue; }
                due = Math.Min(due, SampleRate);

                int n = mixer.Read(floats, 0, (int)due * Channels);
                for (int i = 0; i < n; i++) floats[i] = Math.Clamp(floats[i], -1f, 1f);
                Buffer.BlockCopy(floats, 0, bytes, 0, n * sizeof(float));

                var stream = _ffmpeg?.InputStream;
                if (stream == null) return;
                stream.Write(bytes, 0, n * sizeof(float));
                stream.Flush();
                framesWritten += n / Channels;
            }
        }
        catch (IOException) { if (!_stopping) ReportCrash("ffmpegへの書き込みに失敗しました"); }
        catch (ObjectDisposedException) { }
    }

    private void ReportCrash(string message)
    {
        if (_stopping || Interlocked.Exchange(ref _crashReported, 1) == 1) return;
        Crashed?.Invoke(message);
    }

    private async Task TearDownAsync()
    {
        _stopping = true;
        try { _mic?.Stop(); } catch { }
        try { _loop?.Stop(); } catch { }
        _pump?.Join(2000);
        _pump = null;
        _mic?.Dispose(); _mic = null;
        _loop?.Dispose(); _loop = null;
        if (_ffmpeg != null)
        {
            await _ffmpeg.StopAsync(); // stdinを閉じ → ffmpegがエンコードを終えて終了
            _ffmpeg = null;
        }
    }

    public async ValueTask DisposeAsync() => await TearDownAsync();
}
