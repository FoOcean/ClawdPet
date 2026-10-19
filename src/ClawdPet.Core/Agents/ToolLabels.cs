using System.Text.Json;

namespace ClawdPet.Core.Agents;

/// <summary>Short human labels for tool calls ("运行 npm test", "编辑 App.xaml.cs"…).</summary>
public static class ToolLabels
{
    public const int MaxLength = 48;

    public static string Describe(string name, JsonElement input)
    {
        string? S(string key) => input.ValueKind == JsonValueKind.Object && input.TryGetProperty(key, out var v)
            && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        string File(string key) => S(key) is { } p ? System.IO.Path.GetFileName(p.TrimEnd('\\', '/')) : "";

        string label = name switch
        {
            // Descriptions already read as an action ("Run tests", "安装依赖").
            "Bash" or "PowerShell" => S("description") ?? "运行 " + FirstLine(S("command")),
            "Read" => "读取 " + File("file_path"),
            "Edit" or "MultiEdit" => "编辑 " + File("file_path"),
            "Write" => "写入 " + File("file_path"),
            "NotebookEdit" => "编辑 " + File("notebook_path"),
            "Grep" => "搜索 " + S("pattern"),
            "Glob" => "查找 " + S("pattern"),
            "WebFetch" => "浏览 " + Host(S("url")),
            "WebSearch" => "搜索网页 " + S("query"),
            "Task" or "Agent" => "子代理 " + (S("description") ?? S("subagent_type")),
            "TodoWrite" => "更新计划",
            "AskUserQuestion" => "向你提问",
            "ToolSearch" => "查找工具",
            _ when name.StartsWith("mcp__", StringComparison.Ordinal) => Mcp(name),
            _ => name,
        };
        return Clip(label.Trim());
    }

    public static string Clip(string s, int max = MaxLength)
    {
        s = s.ReplaceLineEndings(" ");
        return s.Length <= max ? s : s[..(max - 1)] + "…";
    }

    static string FirstLine(string? s) => s is null ? "" : s.Split('\n', 2)[0].Trim();

    static string Host(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : url ?? "";

    // mcp__server__tool → "tool · server"
    static string Mcp(string name)
    {
        var parts = name.Split("__");
        return parts.Length >= 3 ? $"{parts[^1].Replace('_', ' ')} · {parts[1]}" : name;
    }
}

/// <summary>Reads a todo / plan list out of a tool call (TodoWrite or a look-alike).</summary>
public static class PlanReader
{
    static readonly string[] TextKeys = ["content", "text", "title", "step", "description", "activeForm"];
    static readonly string[] StatusKeys = ["status", "state"];

    public static IReadOnlyList<PlanItem>? TryRead(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object) return null;
        foreach (var prop in input.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Array) continue;
            var items = new List<PlanItem>();
            foreach (var el in prop.Value.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) { items.Clear(); break; }
                string? text = TextKeys.Select(k => el.TryGetProperty(k, out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null)
                    .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
                if (text is null) { items.Clear(); break; }
                string status = StatusKeys.Select(k => el.TryGetProperty(k, out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null)
                    .FirstOrDefault(t => t != null) ?? "pending";
                items.Add(new PlanItem(text, status));
            }
            if (items.Count > 0) return items;
        }
        return null;
    }
}
