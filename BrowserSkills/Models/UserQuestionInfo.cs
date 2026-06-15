namespace BrowserSkills.Models;

/// <summary>AI 调用 ask_user 时生成的问题信息</summary>
public class UserQuestionInfo
{
    public string QuestionId { get; set; } = "";
    public string Question { get; set; } = "";
    public string QuestionType { get; set; } = "confirmation"; // confirmation | multiple_choice | open_ended
    public string[]? Options { get; set; }
    public string? ContextSummary { get; set; }
    public string? DefaultOption { get; set; }
}
