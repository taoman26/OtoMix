using OtoMix.Models;

namespace OtoMix.Services;

public static class FfmpegCommandBuilder
{
    /// <summary>stdin から受け取る f32le PCM を指定コーデックで保存する引数（取得とミックスはアプリ側で済んでいる）。</summary>
    public static IReadOnlyList<string> BuildPipeEncoder(string outputPath, int sampleRate, int channels)
    {
        var args = new List<string>
        {
            "-hide_banner", "-y",
            "-f", "f32le", "-ar", sampleRate.ToString(), "-ac", channels.ToString(), "-i", "pipe:0",
        };
        args.AddRange(CodecArgs(outputPath));
        args.Add(outputPath);
        return args;
    }

    private static IEnumerable<string> CodecArgs(string outputPath) =>
        Path.GetExtension(outputPath).ToLowerInvariant() switch
        {
            ".mp3" => new[] { "-c:a", "libmp3lame", "-q:a", "2" },
            ".wav" => new[] { "-c:a", "pcm_s16le" },
            var ext => throw new ArgumentException($"未対応の拡張子です（.mp3 / .wav のみ）: '{ext}'", nameof(outputPath)),
        };
}
