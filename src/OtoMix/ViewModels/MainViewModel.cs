using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OtoMix.Models;
using OtoMix.Services;

namespace OtoMix.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public ObservableCollection<AudioDevice> Microphones { get; } = new();
    public ObservableCollection<AudioDevice> Loopbacks { get; } = new();

    [ObservableProperty] private AudioDevice? _selectedMicrophone;
    [ObservableProperty] private AudioDevice? _selectedLoopback;
    [ObservableProperty] private string _outputPath = DefaultOutputPath();

    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(RecordButtonText))]
    [ObservableProperty] private bool _isRecording;

    [ObservableProperty] private string _statusText = "待機中";
    [ObservableProperty] private bool _hasError;

    [ObservableProperty] private bool _isMicMuted;
    [ObservableProperty] private bool _isLoopMuted;

    partial void OnIsMicMutedChanged(bool value) { _recorder.SetMuted(microphone: true, value); RefreshRecordingStatus(); }
    partial void OnIsLoopMutedChanged(bool value) { _recorder.SetMuted(microphone: false, value); RefreshRecordingStatus(); }

    [RelayCommand] private void ToggleMicMute() => IsMicMuted = !IsMicMuted;
    [RelayCommand] private void ToggleLoopMute() => IsLoopMuted = !IsLoopMuted;

    public bool IsIdle => !IsRecording;
    public string RecordButtonText => IsRecording ? "録音停止" : "録音開始";

    private readonly IDeviceProvider _devices;
    private readonly IRecorder _recorder;
    private readonly Stopwatch _elapsed = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    /// <summary>保存先選択ダイアログ。View が実装を差し込む（引数: 現在のパス、戻り値: 選択パス or null）。</summary>
    public Func<string, Task<string?>>? PickSavePath { get; set; }

    public MainViewModel() : this(DeviceProviderFactory.Create()) { }

    public MainViewModel(IDeviceProvider devices, IRecorder? recorder = null)
    {
        _devices = devices;
        _recorder = recorder ?? RecorderFactory.Create();
        _recorder.Crashed += OnRecorderCrashed;
        _timer.Tick += (_, _) => UpdateElapsedText();
    }

    /// <summary>起動時にデバイス一覧を取得してコンボへ反映する。</summary>
    public async Task LoadDevicesAsync()
    {
        try
        {
            var list = await _devices.GetDevicesAsync();
            Microphones.Clear();
            Loopbacks.Clear();
            foreach (var m in list.Microphones) Microphones.Add(m);
            foreach (var l in list.Loopbacks) Loopbacks.Add(l);
            SelectedMicrophone = Microphones.FirstOrDefault();
            SelectedLoopback = Loopbacks.FirstOrDefault();
            if (Microphones.Count == 0 || Loopbacks.Count == 0)
                SetError("デバイスが見つかりません");
        }
        catch (Exception ex)
        {
            SetError($"デバイス取得に失敗: {ex.Message.Split('\n')[0].Trim()}");
        }
    }

    private void SetError(string message)
    {
        HasError = true;
        StatusText = message.ReplaceLineEndings(" ");
    }

    [RelayCommand]
    private async Task ToggleRecordingAsync()
    {
        if (IsRecording) await StopRecordingAsync();
        else await StartRecordingAsync();
    }

    private async Task StartRecordingAsync()
    {
        if (SelectedMicrophone is null || SelectedLoopback is null)
        {
            SetError("マイクとスピーカーを選択してください");
            return;
        }
        if (IsMicMuted && IsLoopMuted)
        {
            SetError("両方ミュート中です。どちらか一方はミュートを解除してください");
            return;
        }
        HasError = false;
        try
        {
            StatusText = "起動中…";
            await _recorder.StartAsync(SelectedMicrophone, SelectedLoopback, OutputPath);
            IsRecording = true;
            _elapsed.Restart();
            UpdateElapsedText();
            _timer.Start();
        }
        catch (Exception ex)
        {
            SetError(ex.Message.Split(" (Parameter")[0]);
        }
    }

    private async Task StopRecordingAsync()
    {
        _timer.Stop();
        StatusText = "保存中…";
        await _recorder.StopAsync();
        IsRecording = false;
        HasError = false;
        StatusText = $"保存しました: {Path.GetFileName(OutputPath)}";
        OutputPath = NextOutputPath(OutputPath); // 次の録音で上書きしないよう新しい名前にする
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (PickSavePath is null) return;
        var picked = await PickSavePath(OutputPath);
        if (!string.IsNullOrWhiteSpace(picked)) OutputPath = picked;
    }

    private void UpdateElapsedText() => StatusText = FormatRecording(_elapsed.Elapsed) + MuteSuffix(IsMicMuted, IsLoopMuted);

    private void RefreshRecordingStatus()
    {
        if (IsRecording) UpdateElapsedText();
    }

    public static string MuteSuffix(bool mic, bool loop) => (mic, loop) switch
    {
        (true, true) => "  ・マイクとスピーカーをミュート",
        (true, false) => "  ・マイクをミュート",
        (false, true) => "  ・スピーカーをミュート",
        _ => "",
    };

    public static string FormatRecording(TimeSpan t) => $"録音中  {(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";

    /// <summary>同じフォルダ・拡張子で、日時入りの新しいファイル名を返す。</summary>
    public static string NextOutputPath(string current, DateTime? now = null)
    {
        var dir = Path.GetDirectoryName(current);
        var ext = Path.GetExtension(current);
        if (string.IsNullOrEmpty(ext)) ext = ".mp3";
        var name = $"OtoMix_{now ?? DateTime.Now:yyyyMMdd_HHmmss}{ext}";
        return string.IsNullOrEmpty(dir) ? name : Path.Combine(dir, name);
    }

    private void OnRecorderCrashed(string message) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _timer.Stop();
            IsRecording = false;
            SetError($"録音が中断されました: {message}");
        });

    private static string DefaultOutputPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Recordings", $"OtoMix_{DateTime.Now:yyyyMMdd_HHmmss}.mp3");
}
