using Avalonia.Controls;
using Avalonia.Platform.Storage;
using OtoMix.ViewModels;

namespace OtoMix.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => { if (DataContext is MainViewModel vm) vm.PickSavePath = PickSavePathAsync; };
        Opened += async (_, _) => { if (DataContext is MainViewModel vm) await vm.LoadDevicesAsync(); };
    }

    private async Task<string?> PickSavePathAsync(string current)
    {
        var dir = Path.GetDirectoryName(current);
        var ext = Path.GetExtension(current).TrimStart('.').ToLowerInvariant();
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "録音ファイルの保存先",
            SuggestedFileName = Path.GetFileName(current),
            SuggestedStartLocation = string.IsNullOrEmpty(dir) || !Directory.Exists(dir) ? null : await StorageProvider.TryGetFolderFromPathAsync(dir),
            DefaultExtension = ext == "wav" ? "wav" : "mp3",
            ShowOverwritePrompt = true,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("MP3") { Patterns = new[] { "*.mp3" } },
                new FilePickerFileType("WAV") { Patterns = new[] { "*.wav" } },
            },
        });
        return file?.TryGetLocalPath();
    }
}
