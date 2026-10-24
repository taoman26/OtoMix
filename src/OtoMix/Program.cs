using Avalonia;
using System;

namespace OtoMix;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // 単純なフォーム1枚でGPUは不要。GPU/ANGLE起動失敗(SwapChain作成エラー)を避け、軽量にするためWindowsはソフトウェア描画
            .With(new Win32PlatformOptions { RenderingMode = new[] { Win32RenderingMode.Software } })
            
            .LogToTrace();
}
