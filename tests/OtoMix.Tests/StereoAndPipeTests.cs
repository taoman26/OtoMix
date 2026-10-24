using System.Diagnostics;
using NAudio.Wave;
using OtoMix.Services;

namespace OtoMix.Tests;

public class StereoAndPipeTests
{
    private sealed class Fixed : ISampleProvider
    {
        private readonly float[] _data;
        public Fixed(int rate, int ch, float[] data) { WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, ch); _data = data; }
        public WaveFormat WaveFormat { get; }
        public int Read(float[] b, int o, int c) { int n = Math.Min(c, _data.Length); Array.Copy(_data, 0, b, o, n); return n; }
    }

    [Fact]
    public void Mono_IsDuplicatedToBothChannels()
    {
        var s = new ToStereoSampleProvider(new Fixed(48000, 1, new[] { 0.1f, 0.2f, 0.3f }));
        var buf = new float[6];
        Assert.Equal(6, s.Read(buf, 0, 6));
        Assert.Equal(new[] { 0.1f, 0.1f, 0.2f, 0.2f, 0.3f, 0.3f }, buf);
        Assert.Equal(2, s.WaveFormat.Channels);
    }

    [Fact]
    public void Surround_UsesFirstTwoChannels()
    {
        var s = new ToStereoSampleProvider(new Fixed(48000, 6, new[] { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, 10f, 11f, 12f }));
        var buf = new float[4];
        Assert.Equal(4, s.Read(buf, 0, 4));
        Assert.Equal(new[] { 1f, 2f, 7f, 8f }, buf);
    }

    // WASAPI経路と同じ「stdinにf32le PCMを流して保存」を、Linuxの実ffmpegで確かめる
    [Theory]
    [InlineData("mp3")]
    [InlineData("wav")]
    public async Task DataPipe_PcmThroughStdin_ProducesValidFile(string ext)
    {
        var path = Path.Combine(Path.GetTempPath(), $"otomix_pipe_{Guid.NewGuid():N}.{ext}");
        await using var rec = new RecorderService();
        await rec.StartAsync(FfmpegCommandBuilder.BuildPipeEncoder(path, 48000, 2));
        var stream = rec.InputStream!;
        var bytes = new byte[48000 * 2 * 4];
        for (int i = 0; i < 48000; i++)
        {
            var v = BitConverter.GetBytes((float)(0.5 * Math.Sin(2 * Math.PI * 440 * i / 48000)));
            Array.Copy(v, 0, bytes, i * 8, 4); Array.Copy(v, 0, bytes, i * 8 + 4, 4);
        }
        for (int sec = 0; sec < 2; sec++) stream.Write(bytes);
        stream.Flush();
        var sw = Stopwatch.StartNew();
        await rec.StopAsync();

        var psi = new ProcessStartInfo("ffprobe") { RedirectStandardOutput = true };
        foreach (var a in new[] { "-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", path }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var dur = double.Parse((await p.StandardOutput.ReadToEndAsync()).Trim(), System.Globalization.CultureInfo.InvariantCulture);
        Console.WriteLine($"[{ext}] stop {sw.ElapsedMilliseconds}ms duration={dur}");
        Assert.InRange(dur, 1.9, 2.2);
        Assert.True(sw.ElapsedMilliseconds < 3000);
        File.Delete(path);
    }
}
