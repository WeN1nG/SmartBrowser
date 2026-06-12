using System.Text.RegularExpressions;

namespace BrowserDemo.Models;

/// <summary>将 Assistant 的可见输出拆成"思考过程"和"结论"两个 UI 分区。</summary>
/// <remarks>
/// 优先解析 [思考过程] / [结论] 标题；同时兼容旧的 <thinking>...</thinking> 和 <conclusion>...</conclusion> 标签。
/// </remarks>
public static partial class AssistantResponseParser
{
    /// <summary>解析 AI 回复，提取思考过程和结论分区。</summary>
    public static AssistantResponseSections Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return new AssistantResponseSections(string.Empty, string.Empty);

        if (TryParseBracketSections(content, out var bracketSections))
            return bracketSections;

        // 兼容旧 XML 标签
        var thinkMatch = ThinkingTagRegex().Match(content);
        var conclusionMatch = ConclusionTagRegex().Match(content);

        if (thinkMatch.Success && conclusionMatch.Success)
        {
            var thinking = thinkMatch.Groups[1].Value.Trim();
            var conclusion = conclusionMatch.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(thinking) && !string.IsNullOrWhiteSpace(conclusion))
                return new AssistantResponseSections(thinking, conclusion);

            if (!string.IsNullOrWhiteSpace(thinking))
                return new AssistantResponseSections(thinking, string.Empty);

            return new AssistantResponseSections(string.Empty, conclusion);
        }

        if (thinkMatch.Success)
        {
            var thinking = thinkMatch.Groups[1].Value.Trim();
            var afterThinking = content[(thinkMatch.Index + thinkMatch.Length)..].Trim();
            return new AssistantResponseSections(thinking, afterThinking);
        }

        if (conclusionMatch.Success)
            return new AssistantResponseSections(string.Empty, conclusionMatch.Groups[1].Value.Trim());

        // 都没有标签 → 整段作为结论，保持普通对话兼容
        return new AssistantResponseSections(string.Empty, content.Trim());
    }

    /// <summary>解析并清理 AI 回复。</summary>
    public static AssistantResponseSections ParseAndClean(string content)
    {
        var result = Parse(content);
        // 对结论再做一轮 JSON 清洗
        if (!string.IsNullOrWhiteSpace(result.Conclusion))
        {
            var cleanedConclusion = StripTrailingJsonWords(result.Conclusion);
            result = new AssistantResponseSections(result.Thinking, cleanedConclusion);
        }
        return result;
    }

    private static bool TryParseBracketSections(string content, out AssistantResponseSections sections)
    {
        var matches = BracketSectionHeaderRegex().Matches(content);
        if (matches.Count == 0)
        {
            sections = default;
            return false;
        }

        var thinkingParts = new List<string>();
        var conclusionParts = new List<string>();

        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var start = match.Index + match.Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : content.Length;
            var sectionText = content[start..end].Trim();
            if (string.IsNullOrWhiteSpace(sectionText))
                continue;

            var sectionName = match.Groups[1].Value;
            if (sectionName == "思考过程")
                thinkingParts.Add(sectionText);
            else if (sectionName == "结论")
                conclusionParts.Add(sectionText);
        }

        sections = new AssistantResponseSections(
            string.Join("\n\n", thinkingParts),
            string.Join("\n\n", conclusionParts));
        return true;
    }

    /// <summary>从结论中剥离尾部的 "JSON 单词"（如 JSON 格式的段落）</summary>
    private static string StripTrailingJsonWords(string text)
    {
        var lines = text.Split('\n');
        var sb = new System.Text.StringBuilder();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (IsJsonBlockStart(trimmed))
                continue;
            sb.AppendLine(line);
        }

        var result = sb.ToString().Trim();
        return string.IsNullOrWhiteSpace(result) ? text : result;
    }

    private static bool IsJsonBlockStart(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        var firstChar = line[0];
        if (firstChar != '{' && firstChar != '[') return false;

        if (line.Length < 15 && line.Contains(':', StringComparison.Ordinal))
            return true;

        var upper = line.ToUpperInvariant();
        return upper.Contains("\"items\"")
            || upper.Contains("\"id\"")
            || upper.Contains("\"title\"")
            || upper.Contains("\"plan\"")
            || upper.Contains("\"notes\"")
            || upper.Contains("\"status\"")
            || upper.Contains("}");
    }

    /// <summary>判断文本中 JSON 字符占比是否超过 50%</summary>
    public static bool IsContentPredominantlyJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var jsonChars = 0;
        foreach (var c in text)
        {
            if (c is '{' or '}' or '[' or ']' or ':')
                jsonChars++;
        }
        return jsonChars > text.Length * 0.08;
    }

    [GeneratedRegex(@"^\s*\[(思考过程|结论)\]\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex BracketSectionHeaderRegex();

    [GeneratedRegex(@"<thinking>\s*(.*?)\s*</thinking>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ThinkingTagRegex();

    [GeneratedRegex(@"<conclusion>\s*(.*?)\s*</conclusion>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ConclusionTagRegex();
}
