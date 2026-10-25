using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using OtoMix.ViewModels;
using OtoMix.Views;

namespace OtoMix.Tests;

public class ScreenshotTests
{
    // 画面確認用。出力先は OTOMIX_SHOT_DIR（未指定ならテスト出力ディレクトリ）。
    private static string OutDir => Environment.GetEnvironmentVariable("OTOMIX_SHOT_DIR") ?? AppContext.BaseDirectory;

    [AvaloniaFact]
    public void Window_HasIcon()
    {
        var win = new MainWindow();
        Assert.NotNull(win.Icon);
    }

    [AvaloniaFact]
    public async Task Render_Idle_And_Recording()
    {
        var vm = new MainViewModel();
        await vm.LoadDevicesAsync();
        var win = new MainWindow { DataContext = vm };
        win.Show();

        Save(win, "shot_idle.png");
        vm.IsRecording = true;
        vm.StatusText = "録音中  00:03:12";
        Save(win, "shot_recording.png");
        vm.IsLoopMuted = true;
        vm.StatusText = "録音中  00:03:12" + MainViewModel.MuteSuffix(false, true);
        Save(win, "shot_muted.png");

        Assert.True(File.Exists(Path.Combine(OutDir, "shot_recording.png")));
    }

    private static void Save(MainWindow win, string name)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var frame = win.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame");
        frame.Save(Path.Combine(OutDir, name));
    }
}
