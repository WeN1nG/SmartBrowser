using System.ComponentModel;
using System.Windows;
using BrowserDemo.Services;

namespace BrowserDemo.Views;

public partial class DownloadsWindow : Window
{
    private bool _allowClose;

    public DownloadsWindow(Window owner)
    {
        InitializeComponent();
        Owner = owner;
        DataContext = DownloadManager.Items;
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, CancelEventArgs args)
    {
        if (_allowClose) return;
        args.Cancel = true;
        Hide();
    }

    public void AllowClose()
    {
        _allowClose = true;
    }

    public new void Show()
    {
        base.Show();
        Activate();
    }

    private void ClearCompleted_Click(object sender, RoutedEventArgs e)
    {
        DownloadManager.ClearCompleted();
    }
}
