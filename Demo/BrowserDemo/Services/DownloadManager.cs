using System.Collections.ObjectModel;
using System.Windows;
using BrowserDemo.Models;

namespace BrowserDemo.Services;

public static class DownloadManager
{
    public static ObservableCollection<DownloadItem> Items { get; } = new();

    public static void Add(DownloadItem item)
    {
        RunOnUiThread(() => Items.Insert(0, item));
    }

    public static void Update(DownloadItem item, Action<DownloadItem> update)
    {
        RunOnUiThread(() => update(item));
    }

    public static void ClearCompleted()
    {
        RunOnUiThread(() =>
        {
            for (var i = Items.Count - 1; i >= 0; i--)
            {
                if (Items[i].State != DownloadItemState.InProgress)
                    Items.RemoveAt(i);
            }
        });
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }
}
