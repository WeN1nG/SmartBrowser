namespace BrowserSkills.Models;

/// <summary>Assistant 回复的 UI 展示分区。</summary>
public readonly record struct AssistantResponseSections(string Thinking, string Conclusion)
{
    public bool HasThinking => !string.IsNullOrWhiteSpace(Thinking);
    public bool HasConclusion => !string.IsNullOrWhiteSpace(Conclusion);
}
