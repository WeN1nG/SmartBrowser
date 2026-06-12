using System.IO;
using System.Text.Json;
using BrowserDemo.Models;

namespace BrowserDemo.Services;

/// <summary>书签持久化服务（JSON 文件存储）</summary>
public static class BookmarkService
{
    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SmartAI-Browser-Demo");

    private static readonly string BookmarkPath = Path.Combine(DataDir, "bookmarks.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    static BookmarkService()
    {
        Directory.CreateDirectory(DataDir);
        Logger.Debug($"书签存储文件: {BookmarkPath}");
    }

    public static List<BookmarkInfo> LoadBookmarks()
    {
        Logger.Debug($"加载书签: {BookmarkPath}");

        if (!File.Exists(BookmarkPath))
        {
            Logger.Debug("书签文件不存在，返回空列表");
            return new();
        }

        try
        {
            var json = File.ReadAllText(BookmarkPath);
            var bookmarks = JsonSerializer.Deserialize<List<BookmarkInfo>>(json, JsonOptions) ?? new();
            Logger.Debug($"书签已加载: {bookmarks.Count} 个");
            return bookmarks
                .Where(x => !string.IsNullOrWhiteSpace(x.Url))
                .ToList();
        }
        catch (Exception ex)
        {
            Logger.Exception($"反序列化书签文件失败: {BookmarkPath}", ex);
            return new();
        }
    }

    public static bool SaveBookmarks(IEnumerable<BookmarkInfo> bookmarks)
    {
        try
        {
            var list = bookmarks.ToList();
            Logger.Debug($"保存书签: {list.Count} 个 → {BookmarkPath}");
            File.WriteAllText(BookmarkPath, JsonSerializer.Serialize(list, JsonOptions));
            return true;
        }
        catch (Exception ex)
        {
            Logger.Exception($"保存书签失败: {BookmarkPath}", ex);
            return false;
        }
    }
}
