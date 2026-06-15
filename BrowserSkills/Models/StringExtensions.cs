namespace BrowserSkills.Models;

/// <summary>
/// 全局字符串扩展方法
/// </summary>
internal static class StringExtensions
{
    /// <summary>截断字符串到指定长度，超出部分加"…"</summary>
    public static string Truncate(this string s, int maxLen)
        => s.Length <= maxLen ? s : s[..maxLen] + "…";
}
