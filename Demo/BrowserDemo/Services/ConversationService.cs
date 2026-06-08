using System.IO;
using System.Text.Json;
using BrowserDemo.Models;

namespace BrowserDemo.Services;

/// <summary>对话持久化服务（JSON 文件存储）</summary>
public class ConversationService
{
    private static readonly string DataDir = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SmartAI-Browser-Demo", "conversations");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    static ConversationService()
    {
        Directory.CreateDirectory(DataDir);
        Logger.Debug($"对话存储目录: {DataDir}");
    }

    /// <summary>获取所有会话摘要</summary>
    public static List<ConversationSummary> ListConversations()
    {
        if (!Directory.Exists(DataDir))
        {
            Logger.Debug("对话目录不存在，返回空列表");
            return new();
        }

        var files = Directory.GetFiles(DataDir, "*.json");
        Logger.Debug($"扫描对话文件: {files.Length} 个");

        return files
            .Select(f =>
            {
                try
                {
                    var name = Path.GetFileNameWithoutExtension(f);
                    var info = new FileInfo(f);
                    return new ConversationSummary
                    {
                        Id = name,
                        FilePath = f,
                        CreatedAt = info.CreationTime,
                        MessageCount = CountMessages(f),
                        Preview = GetFirstMessage(f),
                    };
                }
                catch (Exception ex)
                {
                    Logger.Warning($"读取对话文件失败: {f} — {ex.Message}");
                    return null;
                }
            })
            .Where(x => x != null)
            .OrderByDescending(x => x!.CreatedAt)
            .ToList()!;
    }

    /// <summary>保存会话</summary>
    public static void SaveConversation(string id, List<ChatMessage> messages)
    {
        var path = Path.Combine(DataDir, $"{id}.json");
        Logger.Debug($"保存对话: {id} ({messages.Count} 条消息) → {path}");

        var data = new ConversationData
        {
            Id = id,
            SavedAt = DateTime.Now,
            Messages = messages
        };
        File.WriteAllText(path, JsonSerializer.Serialize(data, JsonOptions));
    }

    /// <summary>加载会话</summary>
    public static List<ChatMessage>? LoadConversation(string id)
    {
        var path = Path.Combine(DataDir, $"{id}.json");
        Logger.Debug($"加载对话: {path}");

        if (!File.Exists(path))
        {
            Logger.Warning($"对话文件不存在: {path}");
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<ConversationData>(json, JsonOptions);
            var count = data?.Messages.Count ?? 0;
            Logger.Debug($"对话已加载: {count} 条消息");
            return data?.Messages;
        }
        catch (Exception ex)
        {
            Logger.Exception($"反序列化对话文件失败: {path}", ex);
            return null;
        }
    }

    /// <summary>删除会话</summary>
    public static void DeleteConversation(string id)
    {
        var path = Path.Combine(DataDir, $"{id}.json");
        Logger.Info($"删除对话: {path}");

        if (File.Exists(path))
        {
            File.Delete(path);
            Logger.Debug("文件已删除");
        }
        else
        {
            Logger.Warning($"对话文件不存在，无法删除: {path}");
        }
    }

    private static int CountMessages(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<ConversationData>(json, JsonOptions);
            return data?.Messages.Count ?? 0;
        }
        catch { return 0; }
    }

    private static string GetFirstMessage(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<ConversationData>(json, JsonOptions);
            var first = data?.Messages?.FirstOrDefault(m => m.Role == MessageRole.User);
            if (first != null)
                return first.Content.Length > 60 ? first.Content[..60] + "…" : first.Content;
            return "(空)";
        }
        catch { return "(加载失败)"; }
    }
}

public class ConversationSummary
{
    public string Id { get; set; } = "";
    public string FilePath { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public int MessageCount { get; set; }
    public string Preview { get; set; } = "";
}

internal class ConversationData
{
    public string Id { get; set; } = "";
    public DateTime SavedAt { get; set; }
    public List<ChatMessage> Messages { get; set; } = new();
}
