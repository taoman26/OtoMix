using OtoMix.Services;

namespace OtoMix.Tests;

public class MuteTests
{
    [Fact]
    public void MuteSampleProvider_FadesAndZeroes()
    {
        bool muted = false;
        var src = new Const(48000, 1, 1f);
        var p = new MuteSampleProvider(src, () => muted);
        var buf = new float[2400];
        p.Read(buf, 0, 2400);
        Assert.All(buf, v => Assert.Equal(1f, v));
        muted = true;
        p.Read(buf, 0, 2400); // 10ms(480)でフェードアウト、以降0
        Assert.True(buf[0] < 1f && buf[0] > 0.9f);
        Assert.Equal(0f, buf[600]);
        Assert.Equal(0f, buf[2399]);
        muted = false;
        p.Read(buf, 0, 2400);
        Assert.Equal(1f, buf[2399]);
    }

    private sealed class Const : NAudio.Wave.ISampleProvider
    {
        private readonly float _v;
        public Const(int rate, int ch, float v) { WaveFormat = NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(rate, ch); _v = v; }
        public NAudio.Wave.WaveFormat WaveFormat { get; }
        public int Read(float[] b, int o, int c) { Array.Fill(b, _v, o, c); return c; }
    }
}
