using System.Diagnostics;
using System.Text;

namespace OtoMix.Services;

public sealed class LinuxDeviceProvider : IDeviceProvider
{
    public async Task<DeviceList> GetDevicesAsync(CancellationToken ct = default)
    {
        try
        {
            return PactlParser.ParseJson(await RunPactl(ct, "-f", "json", "list", "sources"));
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            // JSON非対応の古いpactl
            return PactlParser.ParseShort(await RunPactl(ct, "list", "short", "sources"));
        }
    }

    private static async Task<string> RunPactl(CancellationToken ct, params string[] args)
    {
        var psi = new ProcessStartInfo("pactl")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["LC_ALL"] = "C.UTF-8";

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("pactl を起動できません");
        var errTask = p.StandardError.ReadToEndAsync(ct);
        var output = await p.StandardOutput.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"pactl 失敗 (exit {p.ExitCode}): {(await errTask).Trim()}");
        return output;
    }
}
