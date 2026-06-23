// JsonTransformer.cs
// JSON 数据转换工具 — 格式化、过滤、合并、查询

using System.Text.Json;
using System.Text.Json.Nodes;

if (args.Length == 0)
{
    Console.WriteLine("用法: JsonTransformer <action> [params]");
    Console.WriteLine("  action: format | filter | merge | query | extract");
    Console.WriteLine("  params: JSON 格式的参数");
    Console.WriteLine();
    Console.WriteLine("示例:");
    Console.WriteLine("  JsonTransformer format \"{\\\"compact\\\": true, \\\"data\\\": {\\\"a\\\":1}}\"");
    return;
}

var action = args[0].ToLowerInvariant();
var inputJson = args.Length > 1 ? args[1] : Console.In.ReadToEnd();

try
{
    switch (action)
    {
        case "format":
            FormatJson(inputJson);
            break;
        case "filter":
            var filterKey = args.Length > 2 ? args[2] : null;
            FilterJson(inputJson, filterKey);
            break;
        case "merge":
            if (args.Length < 3) { Console.Error.WriteLine("merge 需要两个 JSON 参数"); return; }
            MergeJson(inputJson, args[2]);
            break;
        case "query":
            if (args.Length < 3) { Console.Error.WriteLine("query 需要 JSONPath 参数"); return; }
            QueryJson(inputJson, args[2]);
            break;
        case "extract":
            if (args.Length < 3) { Console.Error.WriteLine("extract 需要 key 列表参数"); return; }
            ExtractKeys(inputJson, args[2]);
            break;
        default:
            Console.Error.WriteLine($"未知操作: {action}");
            break;
    }
}
catch (JsonException ex)
{
    Console.Error.WriteLine($"JSON 解析错误: {ex.Message}");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"错误: {ex.Message}");
}

static void FormatJson(string json)
{
    var node = JsonNode.Parse(json);
    var options = new JsonSerializerOptions { WriteIndented = true };
    Console.WriteLine(node?.ToJsonString(options));
}

static void FilterJson(string json, string? filterKey)
{
    var doc = JsonDocument.Parse(json);
    var root = doc.RootElement;

    if (root.ValueKind == JsonValueKind.Array)
    {
        var results = new List<JsonElement>();
        foreach (var item in root.EnumerateArray())
        {
            if (filterKey is null || MatchesFilter(item, filterKey))
                results.Add(item);
        }
        Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }
    else if (root.ValueKind == JsonValueKind.Object)
    {
        var filtered = new Dictionary<string, object?>();
        foreach (var prop in root.EnumerateObject())
        {
            if (filterKey is null || prop.Name.Contains(filterKey, StringComparison.OrdinalIgnoreCase))
                filtered[prop.Name] = JsonNode.Parse(prop.Value.GetRawText());
        }
        Console.WriteLine(JsonSerializer.Serialize(filtered, new JsonSerializerOptions { WriteIndented = true }));
    }
}

static bool MatchesFilter(JsonElement element, string filter)
{
    if (element.ValueKind == JsonValueKind.Object)
    {
        foreach (var prop in element.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.String &&
                prop.Value.GetString()?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true)
                return true;
            if (prop.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                return true;
        }
    }
    return false;
}

static void MergeJson(string json1, string json2)
{
    var node1 = JsonNode.Parse(json1);
    var node2 = JsonNode.Parse(json2);

    if (node1 is JsonObject obj1 && node2 is JsonObject obj2)
    {
        foreach (var kvp in obj2)
        {
            obj1[kvp.Key] = kvp.Value?.DeepClone();
        }
        Console.WriteLine(obj1.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
    else
    {
        Console.Error.WriteLine("合并仅支持 JSON 对象");
    }
}

static void QueryJson(string json, string path)
{
    var node = JsonNode.Parse(json);
    if (node is null) { Console.Error.WriteLine("JSON 解析失败"); return; }

    // 简单的点号路径查询 (e.g., "a.b.c")
    var parts = path.Split('.');
    JsonNode? current = node;
    foreach (var part in parts)
    {
        if (current is null) break;
        if (int.TryParse(part, out var index) && current is JsonArray arr)
        {
            current = arr.Count > index ? arr[index] : null;
        }
        else if (current is JsonObject obj)
        {
            current = obj.TryGetPropertyValue(part, out var val) ? val : null;
        }
        else
        {
            current = null;
        }
    }

    Console.WriteLine(current?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null");
}

static void ExtractKeys(string json, string keysArg)
{
    var doc = JsonDocument.Parse(json);
    var root = doc.RootElement;
    var keys = keysArg.Split(',');

    var result = new Dictionary<string, object?>();
    foreach (var key in keys)
    {
        if (root.TryGetProperty(key.Trim(), out var value))
        {
            result[key.Trim()] = JsonNode.Parse(value.GetRawText());
        }
    }

    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
}
