using Avalonia;
using Avalonia.Headless;
using OtoMix;
using OtoMix.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace OtoMix.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
