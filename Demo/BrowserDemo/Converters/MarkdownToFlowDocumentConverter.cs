using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace BrowserDemo.MarkdownConverters;

/// <summary>
/// 将 Markdown 文本转换为 WPF FlowDocument，支持：
/// - # ~ ### 标题（加大加粗、颜色区分层级）
/// - **加粗**、*斜体*、`行内代码`
/// - ```代码块```（灰色背景、等宽字体）
/// - - 无序列表（• 前缀）
/// - 1. 有序列表
/// - |表格|（Markdown 表格 → Table）
/// - 空行分段
/// - 链接 [text](url) 带蓝色可点击样式
/// </summary>
[ValueConversion(typeof(string), typeof(FlowDocument))]
public class MarkdownToFlowDocumentConverter : IValueConverter
{
    private static readonly SolidColorBrush TextBrush = new(Color.FromRgb(221, 221, 221));
    private static readonly SolidColorBrush Heading1Brush = new(Color.FromRgb(255, 200, 100));
    private static readonly SolidColorBrush Heading2Brush = new(Color.FromRgb(175, 215, 255));
    private static readonly SolidColorBrush Heading3Brush = new(Color.FromRgb(180, 220, 180));
    private static readonly SolidColorBrush CodeBgBrush = new(Color.FromRgb(35, 35, 40));
    private static readonly SolidColorBrush LinkBrush = new(Color.FromRgb(80, 160, 255));
    private static readonly SolidColorBrush InlineCodeBrush = new(Color.FromRgb(230, 150, 100));
    private const double DefaultFontSize = 12;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string markdown || string.IsNullOrEmpty(markdown))
            return null;

        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = DefaultFontSize,
            Foreground = TextBrush,
            Background = Brushes.Transparent,
            PagePadding = new Thickness(0),
            LineHeight = 1.4
        };

        var displayMarkdown = markdown.Length > 12000
            ? markdown[^12000..].Insert(0, "...（较早输出已折叠，仅显示最新内容）\n\n")
            : markdown;

        var paragraphs = ParseMarkdown(displayMarkdown);
        foreach (var p in paragraphs)
            doc.Blocks.Add(p);

        return doc;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private List<Block> ParseMarkdown(string markdown)
    {
        var blocks = new List<Block>();
        var lines = markdown.Split('\n');
        var i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];
            var trimmed = line.TrimEnd('\r');

            // 空行
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                i++;
                continue;
            }

            // 代码块 ``` ... ```
            if (trimmed.StartsWith("```"))
            {
                var codeLines = new List<string>();
                i++;
                while (i < lines.Length && !lines[i].TrimEnd('\r').StartsWith("```"))
                {
                    codeLines.Add(lines[i].TrimEnd('\r'));
                    i++;
                }
                i++; // skip closing ```
                blocks.Add(CreateCodeBlock(string.Join("\n", codeLines)));
                continue;
            }

            // 表格 | ... |
            if (trimmed.StartsWith("|") && trimmed.EndsWith("|"))
            {
                var tableLines = new List<string> { trimmed };
                i++;
                while (i < lines.Length && lines[i].TrimEnd('\r').StartsWith("|") && lines[i].TrimEnd('\r').EndsWith("|"))
                {
                    tableLines.Add(lines[i].TrimEnd('\r'));
                    i++;
                }
                var table = CreateTable(tableLines);
                if (table != null) blocks.Add(table);
                continue;
            }

            // 标题 # ~ ######
            var headingMatch = Regex.Match(trimmed, @"^(#{1,6})\s+(.+)$");
            if (headingMatch.Success)
            {
                var level = Math.Min(headingMatch.Groups[1].Value.Length, 3);
                blocks.Add(CreateHeading(headingMatch.Groups[2].Value, level));
                i++;
                continue;
            }

            // 无序列表 - 或 *
            if (trimmed is "- " or "* " || trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
            {
                var listItems = new List<string>();
                while (i < lines.Length)
                {
                    var l = lines[i].TrimEnd('\r');
                    if (l.StartsWith("- ") || l.StartsWith("* "))
                        listItems.Add(l[2..]);
                    else if (l.StartsWith("  ") && listItems.Count > 0)
                        listItems[^1] += "\n" + l.Trim();
                    else break;
                    i++;
                }
                blocks.Add(CreateBulletList(listItems));
                continue;
            }

            // 有序列表 1. 2. ...
            if (Regex.IsMatch(trimmed, @"^\d+\.\s"))
            {
                var listItems = new List<string>();
                while (i < lines.Length)
                {
                    var l = lines[i].TrimEnd('\r');
                    var m = Regex.Match(l, @"^\d+\.\s(.*)");
                    if (m.Success)
                        listItems.Add(m.Groups[1].Value);
                    else break;
                    i++;
                }
                blocks.Add(CreateOrderedList(listItems));
                continue;
            }

            // 普通段落（合并连续行）
            var paraLines = new List<string>();
            while (i < lines.Length)
            {
                var l = lines[i].TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(l) || Regex.IsMatch(l, @"^#{1,6}\s+")
                    || l.StartsWith("```") || l.StartsWith("|") || l.StartsWith("- ") || l.StartsWith("* ")
                    || Regex.IsMatch(l, @"^\d+\.\s"))
                    break;
                paraLines.Add(l);
                i++;
            }
            if (paraLines.Count > 0)
                blocks.Add(CreateParagraph(string.Join("\n", paraLines)));
        }

        return blocks;
    }

    private static Paragraph CreateHeading(string text, int level)
    {
        var brush = level switch
        {
            1 => Heading1Brush,
            2 => Heading2Brush,
            _ => Heading3Brush
        };
        var size = level switch
        {
            1 => 20.0,
            2 => 16.0,
            _ => 14.0
        };

        var para = new Paragraph
        {
            FontSize = size,
            FontWeight = FontWeights.Bold,
            Foreground = brush,
            Margin = new Thickness(0, 6, 0, 3)
        };
        para.Inlines.AddRange(ParseInline(text));
        return para;
    }

    private Paragraph CreateParagraph(string text)
    {
        var para = new Paragraph
        {
            Margin = new Thickness(0, 2, 0, 2),
            TextIndent = 0,
            LineHeight = 1.5
        };
        para.Inlines.AddRange(ParseInline(text));
        return para;
    }

    private static Paragraph CreateCodeBlock(string code)
    {
        var codeDisplay = code.Length > 3000 ? code[..3000] + $"\n\n... (截断，共 {code.Length} 字符)" : code;
        return new Paragraph
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            Foreground = InlineCodeBrush,
            Background = CodeBgBrush,
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 4, 0, 4),
        }.Also(p => p.Inlines.Add(new Run(codeDisplay)));
    }

    private static Paragraph CreateBulletList(List<string> items)
    {
        var para = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };
        foreach (var item in items)
        {
            para.Inlines.Add(new Run("  •  ") { Foreground = Heading1Brush });
            para.Inlines.AddRange(ParseInline(item));
            para.Inlines.Add(new LineBreak());
        }
        return para;
    }

    private static Paragraph CreateOrderedList(List<string> items)
    {
        var para = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };
        for (int idx = 0; idx < items.Count; idx++)
        {
            para.Inlines.Add(new Run($"  {idx + 1}.  ") { Foreground = Heading2Brush });
            para.Inlines.AddRange(ParseInline(items[idx]));
            para.Inlines.Add(new LineBreak());
        }
        return para;
    }

    private static Table? CreateTable(List<string> lines)
    {
        if (lines.Count < 2) return null;

        var rows = new List<List<string>>();
        foreach (var line in lines)
        {
            if (line.Contains("---")) continue; // 分隔行
            var cells = line.Trim('|').Split('|')
                .Select(c => c.Trim())
                .ToList();
            rows.Add(cells);
        }
        if (rows.Count == 0) return null;

        var table = new Table
        {
            Margin = new Thickness(0, 4, 0, 4),
            CellSpacing = 2,
            Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
            BorderThickness = new Thickness(0)
        };

        var colCount = rows.Max(r => r.Count);
        for (int c = 0; c < colCount; c++)
            table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });

        for (int r = 0; r < rows.Count; r++)
        {
            var row = new TableRow();
            for (int c = 0; c < colCount; c++)
            {
                var cellText = c < rows[r].Count ? rows[r][c] : "";
                var cell = new TableCell
                {
                    Padding = new Thickness(4, 2, 4, 2),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 65)),
                    BorderThickness = new Thickness(0.5),
                };
                var p = new Paragraph();
                if (r == 0)
                {
                    p.FontWeight = FontWeights.Bold;
                    p.Foreground = Heading1Brush;
                }
                p.Inlines.AddRange(ParseInline(cellText));
                cell.Blocks.Add(p);
                row.Cells.Add(cell);
            }
            table.RowGroups.Add(new TableRowGroup { Rows = { row } });
        }

        return table;
    }

    /// <summary>
    /// 解析行内格式：**粗体**、*斜体*、`代码`、[链接](url)
    /// 使用正则分段处理，避免嵌套复杂。
    /// </summary>
    private static List<Inline> ParseInline(string text)
    {
        var inlines = new List<Inline>();

        // 按格式标记分段处理
        // 匹配顺序：`行内代码` > [链接](url) > **粗体** > *斜体* > 纯文本
        var pattern = @"(`[^`]+`)|(\[([^\]]+)\]\(([^)]+)\))|(\*\*([^*]+)\*\*)|(\*([^*]+)\*)";
        int last = 0;

        foreach (Match m in Regex.Matches(text, pattern))
        {
            // 前面的普通文本
            if (m.Index > last)
            {
                var plain = text[last..m.Index];
                if (!string.IsNullOrEmpty(plain))
                    inlines.Add(new Run(plain));
            }

            if (m.Groups[1].Success)
            {
                // `行内代码`
                var codeText = m.Groups[1].Value;
                inlines.Add(new Run(codeText[1..^1])
                {
                    FontFamily = new FontFamily("Consolas"),
                    Foreground = InlineCodeBrush,
                    Background = CodeBgBrush,
                    FontSize = DefaultFontSize - 1
                });
            }
            else if (m.Groups[2].Success)
            {
                // [链接](url)
                inlines.Add(new Hyperlink(new Run(m.Groups[3].Value))
                {
                    Foreground = LinkBrush,
                    NavigateUri = new Uri(m.Groups[4].Value),
                    ToolTip = m.Groups[4].Value
                });
            }
            else if (m.Groups[5].Success)
            {
                // **粗体**
                inlines.Add(new Run(m.Groups[6].Value) { FontWeight = FontWeights.Bold });
            }
            else if (m.Groups[7].Success)
            {
                // *斜体*
                inlines.Add(new Run(m.Groups[8].Value) { FontStyle = FontStyles.Italic });
            }

            last = m.Index + m.Length;
        }

        // 剩余文本
        if (last < text.Length)
        {
            var remaining = text[last..];
            if (!string.IsNullOrEmpty(remaining))
                inlines.Add(new Run(remaining));
        }

        return inlines;
    }
}

/// <summary>用于内联操作的扩展方法</summary>
internal static class ParagraphExtensions
{
    public static T Also<T>(this T obj, Action<T> action)
    {
        action(obj);
        return obj;
    }
}
