using NAudio.Wave;

namespace OtoMix.Services;

/// <summary>任意チャンネル数の入力を2ch(L/R)にそろえる。モノラルは両耳へ複製、3ch以上は先頭2chを使う（サラウンドは簡易扱い）。</summary>
public sealed class ToStereoSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _src;
    private float[] _tmp = Array.Empty<float>();

    public ToStereoSampleProvider(ISampleProvider src)
    {
        _src = src;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(src.WaveFormat.SampleRate, 2);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        int ch = _src.WaveFormat.Channels;
        int frames = count / 2;
        if (_tmp.Length < frames * ch) _tmp = new float[frames * ch];
        int got = _src.Read(_tmp, 0, frames * ch) / ch;
        for (int i = 0; i < got; i++)
        {
            float l = _tmp[i * ch];
            float r = ch > 1 ? _tmp[i * ch + 1] : l;
            buffer[offset + i * 2] = l;
            buffer[offset + i * 2 + 1] = r;
        }
        return got * 2;
    }
}

/// <summary>ミュート切替時にプチノイズが出ないよう10msで音量を上下させる。ミュート中も元データは読み捨てて溜めない。</summary>
public sealed class MuteSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _src;
    private readonly Func<bool> _muted;
    private readonly float _step;
    private float _gain = 1f;

    public MuteSampleProvider(ISampleProvider src, Func<bool> muted)
    {
        _src = src;
        _muted = muted;
        _step = 1f / (0.01f * src.WaveFormat.SampleRate);
    }

    public WaveFormat WaveFormat => _src.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int n = _src.Read(buffer, offset, count);
        int ch = WaveFormat.Channels;
        float target = _muted() ? 0f : 1f;
        if (_gain == target && target == 1f) return n;
        for (int i = 0; i < n; i += ch)
        {
            if (_gain < target) _gain = Math.Min(target, _gain + _step);
            else if (_gain > target) _gain = Math.Max(target, _gain - _step);
            for (int c = 0; c < ch; c++) buffer[offset + i + c] *= _gain;
        }
        return n;
    }
}
