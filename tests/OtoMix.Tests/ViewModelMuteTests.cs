using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using OtoMix.Models;
using OtoMix.Services;
using OtoMix.ViewModels;
using OtoMix.Views;

namespace OtoMix.Tests;

public class ViewModelMuteTests
{
    private sealed class FakeRecorder : IRecorder
    {
        public List<(bool mic, bool muted)> Calls { get; } = new();
        public bool IsRunning { get; private set; }
        public event Action<string>? Crashed { add { } remove { } }
        public void SetMuted(bool microphone, bool muted) => Calls.Add((microphone, muted));
        public Task StartAsync(AudioDevice m, AudioDevice l, string o, CancellationToken ct = default) { IsRunning = true; return Task.CompletedTask; }
        public Task StopAsync() { IsRunning = false; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Devs : IDeviceProvider
    {
        public Task<DeviceList> GetDevicesAsync(CancellationToken ct = default) => Task.FromResult(new DeviceList(
            new[] { new AudioDevice("m", "mic", DeviceKind.Microphone) },
            new[] { new AudioDevice("l.monitor", "spk", DeviceKind.Loopback) }));
    }

    [AvaloniaFact]
    public async Task MuteButtons_ToggleState_AndReachRecorder_EvenWhileRecording()
    {
        var rec = new FakeRecorder();
        var vm = new MainViewModel(new Devs(), rec) { OutputPath = "/tmp/x.mp3" };
        var win = new MainWindow { DataContext = vm };
        win.Show();
        await vm.LoadDevicesAsync();
        var micBtn = win.FindControl<Button>("MicMuteButton")!;
        var loopBtn = win.FindControl<Button>("LoopMuteButton")!;

        micBtn.Command!.Execute(null);                       // 録音前にマイクをミュート
        Assert.True(vm.IsMicMuted);
        Assert.True(micBtn.Classes.Contains("muted"));
        Assert.False(loopBtn.Classes.Contains("muted"));

        await vm.ToggleRecordingCommand.ExecuteAsync(null);  // 録音開始
        Assert.True(vm.IsRecording);
        Assert.EndsWith("・マイクをミュート", vm.StatusText);
        Assert.True(micBtn.IsEnabled && loopBtn.IsEnabled);  // 録音中もミュートボタンは有効

        micBtn.Command!.Execute(null);                       // 録音中に解除
        loopBtn.Command!.Execute(null);                      // 録音中にスピーカーをミュート
        Assert.EndsWith("・スピーカーをミュート", vm.StatusText);
        Assert.Equal(new[] { (true, true), (true, false), (false, true) }, rec.Calls);
    }

    [AvaloniaFact]
    public async Task BothMuted_CannotStart()
    {
        var rec = new FakeRecorder();
        var vm = new MainViewModel(new Devs(), rec) { OutputPath = "/tmp/x.mp3" };
        await vm.LoadDevicesAsync();
        vm.IsMicMuted = true;
        vm.IsLoopMuted = true;
        await vm.ToggleRecordingCommand.ExecuteAsync(null);
        Assert.False(vm.IsRecording);
        Assert.False(rec.IsRunning);
        Assert.True(vm.HasError);
        Console.WriteLine("error text: " + vm.StatusText);
    }
}
