using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BrowserDemo.Models;

public class TabInfo : INotifyPropertyChanged
{
    private string _title = "新标签页";
    private string _url = "https://www.bing.com";
    private bool _isLoading;
    private bool _isActive;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string? CoreId { get; set; }

    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); }
    }

    public string Url
    {
        get => _url;
        set { _url = value; OnPropertyChanged(); }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    public bool IsActive
    {
        get => _isActive;
        set { _isActive = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
