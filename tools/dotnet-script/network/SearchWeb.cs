// SearchWeb.cs
// 联网搜索工具 — 支持必应(Bing)和百度搜索引擎，可限定特定网站
//
// 用法:
//   dotnet script SearchWeb.cs -- <query> [engine] [site] [maxResults]
//   dotnet run --project <tmp> -- <query> [engine] [site] [maxResults]
//
// 参数:
//   query       : 搜索关键词 (必需)
//   engine      : 搜索引擎 "bing" | "baidu" (默认 bing)
//   site        : 限定搜索的网站域名，如 "github.com"
//   maxResults  : 最大返回结果数 (默认 10)
//
// 输出: JSON 格式的搜索结果
//
// 示例:
//   SearchWeb.cs "C# async" bing
//   SearchWeb.cs "机器学习教程" baidu "" 5
//   SearchWeb.cs "dotnet" bing "github.com" 3

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

// ── 参数解析 ──────────────────────────────────────────────────────────────

if (args.Length == 0)
{
    Console.Error.WriteLine("用法: SearchWeb <query> [engine] [site] [maxResults]");
    Console.Error.WriteLine("  query      : 搜索关键词");
    Console.Error.WriteLine("  engine     : bing | baidu (默认 bing)");
    Console.Error.WriteLine("  site       : 限定网站域名 (可选)");
    Console.Error.WriteLine("  maxResults : 最大结果数 (默认 10)");
    Console.Error.WriteLine();
    Console.Error.WriteLine("示例:");
    Console.Error.WriteLine("  SearchWeb \"C# 编程\" bing");
    Console.Error.WriteLine("  SearchWeb \"AI教程\" baidu \"zhihu.com\" 5");
    Environment.Exit(1);
    return;
}

var query = args[0];
var engine = args.Length > 1 ? args[1].ToLowerInvariant() : "bing";
var site = args.Length > 2 ? args[2] : "";
var maxResults = args.Length > 3 && int.TryParse(args[3], out var mr) ? Math.Clamp(mr, 1, 50) : 5;

// 不支持的引擎自动回退到 bing，而不是直接报错
if (engine is not ("bing" or "baidu"))
{
    Console.Error.WriteLine($"[WARN] 不支持的搜索引擎: {engine}，自动回退到 bing");
    engine = "bing";
}

// ── 构建搜索 URL ──────────────────────────────────────────────────────────

var searchQuery = query;

// 智能优化查询：对短中文短语添加引号以获得更精确的结果
if (query.Length is >= 2 and <= 10 && Regex.IsMatch(query, @"^[\u4e00-\u9fa5]+$"))
{
    // 纯中文短语，添加引号进行精确匹配
    searchQuery = $"\"{query}\"";
}

if (!string.IsNullOrWhiteSpace(site))
{
    searchQuery += $" site:{site}";
}

var encodedQuery = Uri.EscapeDataString(searchQuery);

// 添加语言和区域参数
var url = engine switch
{
    "bing"  => $"https://cn.bing.com/search?q={encodedQuery}&setlang=zh-CN&cc=CN",
    "baidu" => $"https://www.baidu.com/s?wd={encodedQuery}&rn={maxResults}&ie=utf-8",
    _ => throw new ArgumentException($"未知引擎: {engine}")
};

// ── HTTP 请求 ─────────────────────────────────────────────────────────────

using var http = new HttpClient(new HttpClientHandler
{
    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
    AllowAutoRedirect = true,
    MaxAutomaticRedirections = 5
})
{
    Timeout = TimeSpan.FromSeconds(30)
};

// 随机化 User-Agent 以避免被识别为爬虫
var userAgents = new[]
{
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36",
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:126.0) Gecko/20100101 Firefox/126.0",
    "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36"
};
var random = new Random();
http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgents[random.Next(userAgents.Length)]);
http.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,en-US;q=0.7,en;q=0.3");
http.DefaultRequestHeaders.Referrer = new Uri("https://www.google.com/");

var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
const int maxRetries = 2;

for (int attempt = 0; attempt <= maxRetries; attempt++)
{
try
{
    if (attempt > 0)
    {
        Console.Error.WriteLine($"[INFO] 第 {attempt} 次重试...");
        await Task.Delay(1000 * attempt); // 指数退避
    }

    var response = await http.GetAsync(url);
    response.EnsureSuccessStatusCode();

    var html = await response.Content.ReadAsStringAsync();

    // ── 解析 HTML ─────────────────────────────────────────────────────────

    var results = engine switch
    {
        "bing"  => ParseBingResults(html, maxResults),
        "baidu" => ParseBaiduResults(html, maxResults),
        _ => []
    };

    // ── 输出 JSON ─────────────────────────────────────────────────────────

    var output = new SearchResult
    {
        Timestamp = timestamp,
        Query = query,
        Engine = engine,
        Site = site,
        TotalResults = results.Count,
        Results = results
    };

    var json = JsonSerializer.Serialize(output, JsonCtx.Default.SearchResult);
    Console.WriteLine(json);

    Environment.Exit(0);
    return; // 成功退出
}
catch (HttpRequestException ex) when (attempt < maxRetries)
{
    Console.Error.WriteLine($"[WARN] HTTP 请求失败，将重试: {ex.Message}");
    continue;
}
catch (TaskCanceledException) when (attempt < maxRetries)
{
    Console.Error.WriteLine("[WARN] 请求超时，将重试");
    continue;
}
catch (HttpRequestException ex)
{
    OutputError(timestamp, query, engine, site, $"HTTP 请求失败: {ex.StatusCode} {ex.Message}");
    Environment.Exit(2);
}
catch (TaskCanceledException)
{
    OutputError(timestamp, query, engine, site, "请求超时 (30秒)");
    Environment.Exit(3);
}
catch (Exception ex)
{
    OutputError(timestamp, query, engine, site, $"搜索失败: {ex.Message}");
    Environment.Exit(4);
}
} // end for loop

// ── 必应结果解析 ──────────────────────────────────────────────────────────

static List<SearchItem> ParseBingResults(string html, int maxResults)
{
    var results = new List<SearchItem>();

    // 匹配搜索结果块: <li class="b_algo">...</li>
    var blockPattern = new Regex(
        @"<li\s+class=""b_algo""[^>]*>([\s\S]*?)</li>",
        RegexOptions.IgnoreCase);

    var blocks = blockPattern.Matches(html);

    foreach (Match block in blocks)
    {
        if (results.Count >= maxResults) break;

        var itemHtml = block.Groups[1].Value;

        // 提取链接: <a href="url">...
        var linkMatch = Regex.Match(itemHtml,
            @"<a\s[^>]*href=""(https?://[^""]+)""[^>]*>([\s\S]*?)</a>",
            RegexOptions.IgnoreCase);

        if (!linkMatch.Success) continue;

        var link = WebUtility.HtmlDecode(linkMatch.Groups[1].Value);

        // 提取标题: 只取 <h2> 内的 <a> 或第一个 <a> 的纯文本
        var title = "";
        var h2Match = Regex.Match(itemHtml,
            @"<h2[^>]*>[\s\S]*?<a[^>]*>([\s\S]*?)</a>[\s\S]*?</h2>",
            RegexOptions.IgnoreCase);

        if (h2Match.Success)
        {
            title = StripHtml(h2Match.Groups[1].Value);
        }
        else
        {
            title = StripHtml(linkMatch.Groups[2].Value);
        }

        // 清理标题中可能残留的域名前缀 (如 "microsoft.comhttps://...")
        title = Regex.Replace(title, @"^[\w.-]+\.(com|cn|org|net|io|dev)\s*", "", RegexOptions.IgnoreCase).Trim();

        // 提取摘要: <p> 或 <div class="b_caption"><p>
        var snippet = "";
        var snippetMatch = Regex.Match(itemHtml,
            @"<p[^>]*>([\s\S]*?)</p>",
            RegexOptions.IgnoreCase);

        if (snippetMatch.Success)
        {
            snippet = StripHtml(snippetMatch.Groups[1].Value);
        }

        if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(link))
        {
            results.Add(new SearchItem
            {
                Title = title,
                Link = link,
                Snippet = snippet
            });
        }
    }

    return results;
}

// ── 百度结果解析 ──────────────────────────────────────────────────────────

static List<SearchItem> ParseBaiduResults(string html, int maxResults)
{
    var results = new List<SearchItem>();

    // 百度结果在 <div class="result c-container ..."> 或 id="content_left" 下
    // 尝试多种模式匹配

    // 模式1: 匹配 result 容器
    var blockPattern = new Regex(
        @"<div\s+(?:class=""result\s+c-container[^""]*""|id=""\d+"")[^>]*>([\s\S]*?)</div>\s*(?=<div\s+(?:class=""result\s+c-container|id=""""))",
        RegexOptions.IgnoreCase);

    var blocks = blockPattern.Matches(html);

    // 如果模式1没有匹配到足够的结果，用更宽松的模式
    if (blocks.Count < 3)
    {
        // 模式2: 基于 h3 标题匹配
        blockPattern = new Regex(
            @"<div[^>]*class=""[^""]*c-container[^""]*""[^>]*>([\s\S]*?)</div>\s*(?=<div[^>]*class=""[^""]*c-container|$)",
            RegexOptions.IgnoreCase);
        blocks = blockPattern.Matches(html);
    }

    foreach (Match block in blocks)
    {
        if (results.Count >= maxResults) break;

        var itemHtml = block.Groups[1].Value;

        // 提取标题和链接 (百度链接通常是重定向URL)
        var linkMatch = Regex.Match(itemHtml,
            @"<a\s[^>]*href=""(https?://[^""]+)""[^>]*>([\s\S]*?)</a>",
            RegexOptions.IgnoreCase);

        if (!linkMatch.Success) continue;

        var link = WebUtility.HtmlDecode(linkMatch.Groups[1].Value);
        var title = StripHtml(linkMatch.Groups[2].Value);

        // 提取摘要: 多种百度摘要模式
        var snippet = "";

        // 模式1: <span class="content-right_...">
        var snippetMatch = Regex.Match(itemHtml,
            @"<span\s+class=""content-right[^""]*""[^>]*>([\s\S]*?)</span>",
            RegexOptions.IgnoreCase);

        // 模式2: <div class="c-abstract">
        if (!snippetMatch.Success)
        {
            snippetMatch = Regex.Match(itemHtml,
                @"<div\s+class=""c-abstract[^""]*""[^>]*>([\s\S]*?)</div>",
                RegexOptions.IgnoreCase);
        }

        // 模式3: <span class="c-color-text">
        if (!snippetMatch.Success)
        {
            snippetMatch = Regex.Match(itemHtml,
                @"<span\s+class=""c-color-text[^""]*""[^>]*>([\s\S]*?)</span>",
                RegexOptions.IgnoreCase);
        }

        // 模式4: 任意 <p> 标签
        if (!snippetMatch.Success)
        {
            snippetMatch = Regex.Match(itemHtml,
                @"<p[^>]*>([\s\S]*?)</p>",
                RegexOptions.IgnoreCase);
        }

        if (snippetMatch.Success)
        {
            snippet = StripHtml(snippetMatch.Groups[1].Value);
        }

        if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(link))
        {
            results.Add(new SearchItem
            {
                Title = title,
                Link = link,
                Snippet = snippet
            });
        }
    }

    // 如果正则解析结果不够，尝试更简单的 h3 链接提取
    if (results.Count == 0)
    {
        var h3Pattern = new Regex(
            @"<h3[^>]*>\s*<a\s[^>]*href=""(https?://[^""]+)""[^>]*>([\s\S]*?)</a>\s*</h3>",
            RegexOptions.IgnoreCase);

        foreach (Match m in h3Pattern.Matches(html))
        {
            if (results.Count >= maxResults) break;

            var link = WebUtility.HtmlDecode(m.Groups[1].Value);
            var title = StripHtml(m.Groups[2].Value);

            if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(link))
            {
                results.Add(new SearchItem
                {
                    Title = title,
                    Link = link,
                    Snippet = ""
                });
            }
        }
    }

    return results;
}

// ── 工具方法 ──────────────────────────────────────────────────────────────

static string StripHtml(string html)
{
    if (string.IsNullOrWhiteSpace(html)) return "";

    // 移除 HTML 标签
    var text = Regex.Replace(html, @"<[^>]+>", "");
    // 解码 HTML 实体
    text = WebUtility.HtmlDecode(text);
    // 合并多余空白
    text = Regex.Replace(text, @"\s+", " ").Trim();
    return text;
}

static void OutputError(string timestamp, string query, string engine, string site, string error)
{
    var output = new SearchResult
    {
        Timestamp = timestamp,
        Query = query,
        Engine = engine,
        Site = site,
        TotalResults = 0,
        Error = error,
        Results = []
    };
    var json = JsonSerializer.Serialize(output, JsonCtx.Default.SearchResult);
    Console.WriteLine(json);
}

// ── 数据模型 ──────────────────────────────────────────────────────────────

public class SearchItem
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("link")]
    public string Link { get; set; } = "";

    [JsonPropertyName("snippet")]
    public string Snippet { get; set; } = "";
}

public class SearchResult
{
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = "";

    [JsonPropertyName("query")]
    public string Query { get; set; } = "";

    [JsonPropertyName("engine")]
    public string Engine { get; set; } = "";

    [JsonPropertyName("site")]
    public string Site { get; set; } = "";

    [JsonPropertyName("totalResults")]
    public int TotalResults { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("results")]
    public List<SearchItem> Results { get; set; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SearchResult))]
internal partial class JsonCtx : JsonSerializerContext { }
