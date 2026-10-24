using Avalonia.Headless.XUnit;
using OtoMix.ViewModels;

namespace OtoMix.Tests;

public class ViewModelRecordingTests
{
    [AvaloniaFact]
    public async Task Toggle_StartsAndStops_RealRecording()
    {
        if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("PULSE_SERVER") is null) return;
        var vm = new MainViewModel();
        await vm.LoadDevicesAsync();
        vm.SelectedMicrophone ??= vm.Loopbacks[0]; // WSLgでマイクが無い場合の代用
        var path = Path.Combine(Path.GetTempPath(), $"otomix_vm_{Guid.NewGuid():N}.mp3");
        vm.OutputPath = path;

        await vm.ToggleRecordingCommand.ExecuteAsync(null);
        Console.WriteLine($"after start: rec={vm.IsRecording} text='{vm.StatusText}' btn='{vm.RecordButtonText}' idle={vm.IsIdle}");
        Assert.True(vm.IsRecording);
        Assert.Equal("録音停止", vm.RecordButtonText);
        await Task.Delay(2000);

        await vm.ToggleRecordingCommand.ExecuteAsync(null);
        Console.WriteLine($"after stop: rec={vm.IsRecording} text='{vm.StatusText}' size={new FileInfo(path).Length}");
        Assert.False(vm.IsRecording);
        Assert.False(vm.HasError);
        Assert.True(new FileInfo(path).Length > 1000);
        File.Delete(path);
    }

    [AvaloniaFact]
    public async Task BadExtension_ShowsError_StaysIdle()
    {
        var vm = new MainViewModel(new Stub());
        await vm.LoadDevicesAsync();
        vm.OutputPath = "/tmp/x.ogg";
        await vm.ToggleRecordingCommand.ExecuteAsync(null);
        Console.WriteLine("error text: " + vm.StatusText);
        Assert.False(vm.IsRecording);
        Assert.True(vm.HasError);
    }

    private sealed class Stub : OtoMix.Services.IDeviceProvider
    {
        public Task<OtoMix.Services.DeviceList> GetDevicesAsync(CancellationToken ct = default) =>
            Task.FromResult(new OtoMix.Services.DeviceList(
                new[] { new OtoMix.Models.AudioDevice("m", "m", OtoMix.Models.DeviceKind.Microphone) },
                new[] { new OtoMix.Models.AudioDevice("l.monitor", "l", OtoMix.Models.DeviceKind.Loopback) }));
    }
}
