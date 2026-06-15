using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BrowserSkills.Models;

/// <summary>AI 实时任务清单项</summary>
public class AiTodoItem : INotifyPropertyChanged
{
    private string _id = string.Empty;
    private string _title = string.Empty;
    private string _status = "pending";
    private string? _notes;

    public string Id
    {
        get => _id;
        set { _id = value; OnPropertyChanged(); }
    }

    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); }
    }

    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusLabel)); }
    }

    public string? Notes
    {
        get => _notes;
        set { _notes = value; OnPropertyChanged(); }
    }

    public string StatusLabel => Status switch
    {
        "in_progress" => "进行中",
        "completed" => "已完成",
        "blocked" => "受阻",
        _ => "待办"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
