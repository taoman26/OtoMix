using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using OtoMix.Models;

namespace OtoMix.Services;

/// <summary>Windows: マイクは WasapiCapture、スピーカー(再生デバイス)は WasapiLoopbackCapture。</summary>
[SupportedOSPlatform("windows")]
public sealed class WasapiCaptureSource : ICaptureSource
{
    private readonly WasapiCapture _capture;

    public WasapiCaptureSource(AudioDevice device)
    {
        using var enumerator = new MMDeviceEnumerator();
        MMDevice dev;
        try { dev = enumerator.GetDevice(device.Id); }
        catch (COMException ex) { throw new InvalidOperationException($"デバイスを開けません: {device.DisplayName}", ex); }
        using (dev)
            _capture = device.Kind == DeviceKind.Loopback ? new WasapiLoopbackCapture(dev) : new WasapiCapture(dev);
        _capture.DataAvailable += (_, e) => DataAvailable?.Invoke(e.Buffer, e.BytesRecorded);
        _capture.RecordingStopped += (_, e) => Stopped?.Invoke(e.Exception);
    }

    public WaveFormat WaveFormat => _capture.WaveFormat;
    public event Action<byte[], int>? DataAvailable;
    public event Action<Exception?>? Stopped;
    public void Start() => _capture.StartRecording();
    public void Stop() => _capture.StopRecording();
    public void Dispose() => _capture.Dispose();
}
