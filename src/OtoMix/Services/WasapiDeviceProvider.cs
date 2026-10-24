using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using OtoMix.Models;

namespace OtoMix.Services;

/// <summary>
/// Windows: WASAPI でデバイス列挙。マイク=キャプチャ端末、スピーカー=再生端末（WASAPIループバックの対象）。
/// Id にはエンドポイントID（表示名が重複しても一意）を入れる。既定デバイスを先頭にする。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WasapiDeviceProvider : IDeviceProvider
{
    public Task<DeviceList> GetDevicesAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        using var enumerator = new MMDeviceEnumerator();
        return new DeviceList(
            List(enumerator, DataFlow.Capture, DeviceKind.Microphone),
            List(enumerator, DataFlow.Render, DeviceKind.Loopback));
    }, ct);

    private static List<AudioDevice> List(MMDeviceEnumerator e, DataFlow flow, DeviceKind kind)
    {
        string? defaultId = null;
        try
        {
            using var def = e.GetDefaultAudioEndpoint(flow, Role.Console);
            defaultId = def.ID;
        }
        catch (System.Runtime.InteropServices.COMException) { /* 既定デバイスなし */ }

        var result = new List<AudioDevice>();
        foreach (var d in e.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            using (d) result.Add(new AudioDevice(d.ID, d.FriendlyName, kind));
        }
        return result.OrderByDescending(d => d.Id == defaultId).ThenBy(d => d.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
