using System.IO;
using System.Text.Json;
using BrowserDemo.Models;

namespace BrowserDemo.Services;

/// <summary>历史记录持久化服务（JSON 文件存储）</summary>
public static class HistoryService
{
    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SmartAI-Browser-Demo");

    private static readonly string HistoryPath = Path.Combine(DataDir, "history.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    static HistoryService()
    {
        Directory.CreateDirectory(DataDir);
        Logger.Debug($"历史记录存储文件: {HistoryPath}");
    }

    public static List<HistoryInfo> LoadHistory()
    {
        Logger.Debug($"加载历史记录: {HistoryPath}");

        if (!File.Exists(HistoryPath))
        {
            Logger.Debug("历史记录文件不存在，返回空列表");
            return new();
        }

        try
        {
            var json = File.ReadAllText(HistoryPath);
            var history = JsonSerializer.Deserialize<List<HistoryInfo>>(json, JsonOptions) ?? new();
            Logger.Debug($"历史记录已加载: {history.Count} 条");
            return history
                .Where(x => !string.IsNullOrWhiteSpace(x.Url))
                .OrderByDescending(x => x.VisitedAt)
                .ToList();
        }
        catch (Exception ex)
        {
            Logger.Exception($"反序列化历史记录文件失败: {HistoryPath}", ex);
            return new();
        }
    }

    public static bool SaveHistory(IEnumerable<HistoryInfo> history)
    {
        try
        {
            var list = history.ToList();
            Logger.Debug($"保存历史记录: {list.Count} 条 → {HistoryPath}");
            File.WriteAllText(HistoryPath, JsonSerializer.Serialize(list, JsonOptions));
            return true;
        }
        catch (Exception ex)
        {
            Logger.Exception($"保存历史记录失败: {HistoryPath}", ex);
            return false;
        }
    }
}
