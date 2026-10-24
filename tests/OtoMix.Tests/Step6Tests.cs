using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OtoMix.ViewModels;
using OtoMix.Views;

namespace OtoMix.Tests;

public class Step6Tests
{
    [Fact]
    public void FormatRecording_PadsAndRollsOverHours() =>
        Assert.Equal("録音中  01:02:03", MainViewModel.FormatRecording(new TimeSpan(1, 2, 3)));

    [Fact]
    public void NextOutputPath_KeepsDirAndExtension()
    {
        var n = MainViewModel.NextOutputPath("/home/u/Rec/meeting.wav", new DateTime(2026, 10, 24, 13, 5, 9));
        Assert.Equal("/home/u/Rec/OtoMix_20261024_130509.wav", n);
    }

    [AvaloniaFact]
    public async Task BrowseButton_UsesPickedPath_AndCancelKeepsOld()
    {
        var vm = new MainViewModel(new NoDevices());
        var win = new MainWindow { DataContext = vm };
        win.Show();
        var asked = "";
        vm.PickSavePath = cur => { asked = cur; return Task.FromResult<string?>("/tmp/picked/会議.wav"); };
        var browse = win.FindControl<Button>("BrowseButton")!;
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)browse.Command!).ExecuteAsync(null);
        Assert.Equal("/tmp/picked/会議.wav", vm.OutputPath);
        Assert.EndsWith(".mp3", asked);

        vm.PickSavePath = _ => Task.FromResult<string?>(null);
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)browse.Command!).ExecuteAsync(null);
        Assert.Equal("/tmp/picked/会議.wav", vm.OutputPath);
    }

    [AvaloniaFact]
    public async Task RealRecording_Timer_Ticks_ControlsDisabled_ThenReenabled()
    {
        if (Environment.GetEnvironmentVariable("PULSE_SERVER") is null) return;
        var vm = new MainViewModel();
        await vm.LoadDevicesAsync();
        vm.SelectedMicrophone ??= vm.Loopbacks[0];
        var dir = Path.Combine(Path.GetTempPath(), $"otomix_t6_{Guid.NewGuid():N}");
        vm.OutputPath = Path.Combine(dir, "a.mp3");
        var win = new MainWindow { DataContext = vm };
        win.Show();
        var combo = win.GetVisualDescendants().OfType<ComboBox>().First();
        var shotDir = Environment.GetEnvironmentVariable("OTOMIX_SHOT_DIR") ?? AppContext.BaseDirectory;

        var rec = (CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)win.FindControl<Button>("RecordButton")!.Command!;
        await rec.ExecuteAsync(null);
        var seen = new HashSet<string>();
        for (int i = 0; i < 14; i++) { await Task.Delay(250); Dispatcher.UIThread.RunJobs(); seen.Add(vm.StatusText); }
        Console.WriteLine("status seen: " + string.Join(" | ", seen));
        Assert.False(combo.IsEnabled);
        Assert.Contains("録音中  00:00:03", seen);
        win.CaptureRenderedFrame()!.Save(Path.Combine(shotDir, "shot_recording_live.png"));

        await rec.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(combo.IsEnabled);
        Assert.NotEqual(Path.Combine(dir, "a.mp3"), vm.OutputPath);
        Assert.StartsWith(dir, vm.OutputPath);
        var before = vm.StatusText;
        await Task.Delay(600); Dispatcher.UIThread.RunJobs();
        Assert.Equal(before, vm.StatusText); // タイマー停止済み
        Assert.True(new FileInfo(Path.Combine(dir, "a.mp3")).Length > 1000);
        Directory.Delete(dir, true);
    }

    private sealed class NoDevices : OtoMix.Services.IDeviceProvider
    {
        public Task<OtoMix.Services.DeviceList> GetDevicesAsync(CancellationToken ct = default) =>
            Task.FromResult(new OtoMix.Services.DeviceList(Array.Empty<OtoMix.Models.AudioDevice>(), Array.Empty<OtoMix.Models.AudioDevice>()));
    }
}
