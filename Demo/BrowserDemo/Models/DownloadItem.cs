using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BrowserDemo.Models;

public class DownloadItem : INotifyPropertyChanged
{
    private string _fileName = "下载文件";
    private string _uri = string.Empty;
    private string _resultFilePath = string.Empty;
    private long _bytesReceived;
    private long? _totalBytesToReceive;
    private DownloadItemState _state = DownloadItemState.InProgress;

    public Guid Id { get; } = Guid.NewGuid();
    public DateTime StartedAt { get; } = DateTime.Now;

    public string FileName
    {
        get => _fileName;
        set { _fileName = value; OnPropertyChanged(); }
    }

    public string Uri
    {
        get => _uri;
        set { _uri = value; OnPropertyChanged(); }
    }

    public string ResultFilePath
    {
        get => _resultFilePath;
        set { _resultFilePath = value; OnPropertyChanged(); }
    }

    public long BytesReceived
    {
        get => _bytesReceived;
        set
        {
            _bytesReceived = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProgressPercent));
            OnPropertyChanged(nameof(SizeText));
        }
    }

    public long? TotalBytesToReceive
    {
        get => _totalBytesToReceive;
        set
        {
            _totalBytesToReceive = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProgressPercent));
            OnPropertyChanged(nameof(SizeText));
        }
    }

    public DownloadItemState State
    {
        get => _state;
        set
        {
            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StateText));
        }
    }

    public int ProgressPercent
    {
        get
        {
            if (!TotalBytesToReceive.HasValue || TotalBytesToReceive.Value <= 0) return 0;
            return (int)Math.Clamp(BytesReceived * 100.0 / TotalBytesToReceive.Value, 0, 100);
        }
    }

    public string SizeText
    {
        get
        {
            var received = FormatBytes(BytesReceived);
            return TotalBytesToReceive.HasValue && TotalBytesToReceive.Value > 0
                ? $"{received} / {FormatBytes(TotalBytesToReceive.Value)}"
                : received;
        }
    }

    public string StateText => State switch
    {
        DownloadItemState.InProgress => "下载中",
        DownloadItemState.Completed => "已完成",
        DownloadItemState.Canceled => "已取消",
        DownloadItemState.Failed => "失败",
        _ => "未知"
    };

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.##} {units[unit]}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public enum DownloadItemState
{
    InProgress,
    Completed,
    Canceled,
    Failed
}
