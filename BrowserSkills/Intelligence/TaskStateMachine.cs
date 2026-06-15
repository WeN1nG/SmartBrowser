using BrowserSkills.Models;

namespace BrowserSkills.Intelligence;

/// <summary>
/// 浏览器自动化任务状态机 —— 强制 AI 按任务清单步骤严格执行。
///
/// 状态流转：
///   Planning → Executing（AI 调用 update_todo 建立清单）
///   Executing → Complete（所有子任务 completed/blocked）
///
/// 规则：
///   1. update_todo 只能在 Planning 状态调用
///   2. start_subtask 只能对当前 ActiveSubtaskId
///   3. finish_subtask 只能对当前 ActiveSubtaskId
///   4. 下一子任务由列表顺序自动决定
///   5. 执行中调用 update_todo 被拒绝
/// </summary>
public class TaskStateMachine
{
    /// <summary>当前状态</summary>
    public TaskState CurrentState { get; private set; } = TaskState.Planning;

    /// <summary>当前正在执行的子任务 ID（仅 Executing 状态下有意义）</summary>
    public string? ActiveSubtaskId { get; private set; }

    /// <summary>完整任务清单（顺序固定，不可跳过）</summary>
    public IReadOnlyList<TaskItem> TodoItems => _items.AsReadOnly();

    /// <summary>是否所有子任务均已结束</summary>
    public bool IsComplete => CurrentState == TaskState.Complete;

    private readonly List<TaskItem> _items = new();

    /// <summary>重置状态机到初始状态</summary>
    public void Reset()
    {
        CurrentState = TaskState.Planning;
        ActiveSubtaskId = null;
        _items.Clear();
    }

    /// <summary>
    /// 处理 AI 的 update_todo 调用，建立任务清单。
    /// 仅在 Planning 状态下允许。
    /// </summary>
    public TransitionResult ProcessTodoUpdate(IReadOnlyList<AiTodoItem> items)
    {
        if (CurrentState != TaskState.Planning)
        {
            return TransitionResult.Rejected(
                CurrentState == TaskState.Complete
                    ? "所有子任务已完成，不能新建任务清单。如需新任务请开启新对话。"
                    : $"当前正在执行子任务（{ActiveSubtaskId}），必须先 finish_subtask 当前子任务后才能更新任务清单。");
        }

        if (items == null || items.Count == 0)
            return TransitionResult.Rejected("任务清单不能为空。");

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var taskItems = new List<TaskItem>();
        var isFirst = true;

        foreach (var item in items)
        {
            var id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N")[..8] : item.Id.Trim();
            var title = item.Title?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(title))
                continue;

            if (!seenIds.Add(id))
                return TransitionResult.Rejected($"任务清单中存在重复的 ID: {id}。");

            var status = NormalizeTodoStatus(item.Status);
            var taskItem = new TaskItem { Id = id, Title = title, Status = status };

            // 第一个子任务自动标记为 in_progress（因为 update_todo 本身就是"我要开始执行了"的信号）
            if (isFirst)
            {
                taskItem.Status = "in_progress";
                isFirst = false;
            }

            taskItems.Add(taskItem);
        }

        if (taskItems.Count == 0)
            return TransitionResult.Rejected("任务清单中没有有效的任务项。");

        _items.Clear();
        _items.AddRange(taskItems);

        CurrentState = TaskState.Executing;
        ActiveSubtaskId = _items[0].Id;

        return TransitionResult.Accepted(taskItems);
    }

    /// <summary>
    /// 处理 AI 的 start_subtask 调用。
    /// 仅允许对当前 ActiveSubtaskId 调用。
    /// </summary>
    public TransitionResult ProcessStartSubtask(string subtaskId)
    {
        if (CurrentState != TaskState.Executing)
            return TransitionResult.Rejected("尚未建立任务清单，请先调用 update_todo 创建完整子任务清单。");

        if (string.IsNullOrEmpty(subtaskId))
            return TransitionResult.Rejected("start_subtask 的 id 不能为空。");

        if (subtaskId != ActiveSubtaskId)
        {
            var currentItem = _items.FirstOrDefault(t => t.Id == ActiveSubtaskId);
            var currentTitle = currentItem?.Title ?? ActiveSubtaskId;
            return TransitionResult.Rejected(
                $"必须先完成当前子任务「{currentTitle}」，才能开始其他子任务。");
        }

        var target = _items.FirstOrDefault(t => t.Id == subtaskId);
        if (target == null)
            return TransitionResult.Rejected($"任务清单中未找到 ID 为 {subtaskId} 的子任务。");

        if (target.Status is "completed" or "blocked")
            return TransitionResult.Rejected(
                $"子任务「{target.Title}」已标记为 {target.Status}，不能重复开始。");

        target.Status = "in_progress";

        return TransitionResult.AcceptedWithCompression(_items, CompressionLevel.Standard);
    }

    /// <summary>
    /// 处理 AI 的 finish_subtask 调用。
    /// 仅允许对当前 ActiveSubtaskId 调用，完成后自动启动下一个 pending 子任务。
    /// </summary>
    public TransitionResult ProcessFinishSubtask(string subtaskId, string status)
    {
        if (CurrentState != TaskState.Executing)
            return TransitionResult.Rejected("当前没有正在执行的子任务。");

        if (string.IsNullOrEmpty(subtaskId))
            return TransitionResult.Rejected("finish_subtask 的 id 不能为空。");

        if (subtaskId != ActiveSubtaskId)
        {
            var currentItem = _items.FirstOrDefault(t => t.Id == ActiveSubtaskId);
            var currentTitle = currentItem?.Title ?? ActiveSubtaskId;
            return TransitionResult.Rejected(
                $"当前正在执行「{currentTitle}」，必须先完成它。");
        }

        if (status != "completed" && status != "blocked")
            return TransitionResult.Rejected("finish_subtask 的 status 必须是 'completed' 或 'blocked'。");

        var target = _items.FirstOrDefault(t => t.Id == subtaskId);
        if (target == null)
            return TransitionResult.Rejected($"任务清单中未找到 ID 为 {subtaskId} 的子任务。");

        target.Status = status;

        // 找到下一个 pending 子任务
        var nextItem = _items
            .SkipWhile(t => t.Id != subtaskId)
            .Skip(1)
            .FirstOrDefault(t => t.Status == "pending");

        if (nextItem != null)
        {
            nextItem.Status = "in_progress";
            ActiveSubtaskId = nextItem.Id;
        }
        else
        {
            // 所有子任务已结束
            CurrentState = TaskState.Complete;
            ActiveSubtaskId = null;
        }

        var compression = (status == "completed" && nextItem != null)
            ? CompressionLevel.Max
            : CompressionLevel.None;

        return TransitionResult.AcceptedWithCompression(_items, compression);
    }

    private static string NormalizeTodoStatus(string? status) => status switch
    {
        "pending" or "in_progress" or "completed" or "blocked" => status,
        "doing" or "active" or "running" => "in_progress",
        "done" or "success" or "finished" => "completed",
        "failed" or "error" => "blocked",
        _ => "pending"
    };
}

/// <summary>状态机状态</summary>
public enum TaskState
{
    /// <summary>等待 AI 调用 update_todo 建立任务清单</summary>
    Planning,

    /// <summary>正在执行某个子任务</summary>
    Executing,

    /// <summary>所有子任务已结束</summary>
    Complete
}

/// <summary>压缩级别</summary>
public enum CompressionLevel
{
    /// <summary>不压缩</summary>
    None,

    /// <summary>标准压缩（子任务开始前）</summary>
    Standard,

    /// <summary>最大压缩（子任务完成后）</summary>
    Max
}

/// <summary>子任务项</summary>
public class TaskItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "pending";
}

/// <summary>状态机转换结果</summary>
public class TransitionResult
{
    public bool Valid { get; }
    public string? Error { get; }
    public CompressionLevel Compression { get; }
    public IReadOnlyList<TaskItem> TodoItems { get; }

    private TransitionResult(bool valid, string? error, CompressionLevel compression, IReadOnlyList<TaskItem> items)
    {
        Valid = valid;
        Error = error;
        Compression = compression;
        TodoItems = items;
    }

    public static TransitionResult Accepted(IReadOnlyList<TaskItem> items)
        => new(true, null, CompressionLevel.None, items);

    public static TransitionResult AcceptedWithCompression(IReadOnlyList<TaskItem> items, CompressionLevel compression)
        => new(true, null, compression, items);

    public static TransitionResult Rejected(string reason)
        => new(false, reason, CompressionLevel.None, Array.Empty<TaskItem>());
}
