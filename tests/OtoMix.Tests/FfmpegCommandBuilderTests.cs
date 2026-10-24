using OtoMix.Services;

namespace OtoMix.Tests;

public class FfmpegCommandBuilderTests
{
    [Fact]
    public void PipeEncoder_Wav_AndMp3()
    {
        Assert.Equal(new[]
        {
            "-hide_banner", "-y", "-f", "f32le", "-ar", "48000", "-ac", "2", "-i", "pipe:0",
            "-c:a", "libmp3lame", "-q:a", "2", @"C:
\会議.mp3",
        }, FfmpegCommandBuilder.BuildPipeEncoder(@"C:
\会議.mp3", 48000, 2));
        Assert.Contains("pcm_s16le", FfmpegCommandBuilder.BuildPipeEncoder("a.WAV", 48000, 2));
    }

    [Fact]
    public void UnsupportedExtension_Throws() =>
        Assert.Throws<ArgumentException>(() => FfmpegCommandBuilder.BuildPipeEncoder("/tmp/o.flac", 48000, 2));
}
