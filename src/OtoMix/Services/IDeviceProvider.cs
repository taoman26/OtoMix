using OtoMix.Models;

namespace OtoMix.Services;

public sealed record DeviceList(IReadOnlyList<AudioDevice> Microphones, IReadOnlyList<AudioDevice> Loopbacks);

public interface IDeviceProvider
{
    Task<DeviceList> GetDevicesAsync(CancellationToken ct = default);
}

public static class DeviceProviderFactory
{
    public static IDeviceProvider Create() =>
        OperatingSystem.IsWindows() ? new WasapiDeviceProvider() : new LinuxDeviceProvider();
}
