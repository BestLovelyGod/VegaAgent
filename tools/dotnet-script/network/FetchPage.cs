// FetchPage.cs
// 网页抓取工具 — 获取 URL 内容并提取可读文本
//
// 用法:
//   dotnet script FetchPage.cs -- <url> [maxChars] [format]
//
// 参数:
//   url       : 目标 URL (必需)
//   maxChars  : 最大输出字符数 (默认 8000)
//   format    : 输出格式 "text" | "markdown" (默认 text)
//
// 输出: JSON 格式的页面内容
//
// 示例:
//   FetchPage.cs "https://github.com/dotnet/runtime"
//   FetchPage.cs "https://learn.microsoft.com/dotnet/csharp" 4000
//   FetchPage.cs "https://example.com" 2000 markdown

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

// ── 参数解析 ──────────────────────────────────────────────────────────────

if (args.Length == 0)
{
    Console.Error.WriteLine("用法: FetchPage <url> [maxChars] [format]");
    Console.Error.WriteLine("  url      : 目标 URL");
    Console.Error.WriteLine("  maxChars : 最大输出字符数 (默认 8000)");
    Console.Error.WriteLine("  format   : text | markdown (默认 text)");
    Console.Error.WriteLine();
    Console.Error.WriteLine("示例:");
    Console.Error.WriteLine("  FetchPage \"https://github.com/dotnet/runtime\"");
    Console.Error.WriteLine("  FetchPage \"https://example.com\" 2000 markdown");
    Environment.Exit(1);
    return;
}

var url = args[0];
var maxChars = args.Length > 1 && int.TryParse(args[1], out var mc) ? Math.Clamp(mc, 100, 100_000) : 8000;
var format = args.Length > 2 ? args[2].ToLowerInvariant() : "text";

if (format is not ("text" or "markdown"))
{
    Console.Error.WriteLine($"[WARN] 不支持的格式: {format}，自动回退到 text");
    format = "text";
}

// URL 合法性检查
if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
{
    OutputError(url, $"无效的 URL: {url}");
    Environment.Exit(1);
    return;
}

// ── HTTP 请求 ─────────────────────────────────────────────────────────────

var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
const int maxRetries = 2;

using var http = new HttpClient(new HttpClientHandler
{
    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
    AllowAutoRedirect = true,
    MaxAutomaticRedirections = 5
})
{
    Timeout = TimeSpan.FromSeconds(20)
};

var userAgents = new[]
{
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36",
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:126.0) Gecko/20100101 Firefox/126.0",
    "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36"
};
var random = new Random();
http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgents[random.Next(userAgents.Length)]);
http.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,en-US;q=0.7,en;q=0.3");

for (int attempt = 0; attempt <= maxRetries; attempt++)
{
    try
    {
        if (attempt > 0)
        {
            Console.Error.WriteLine($"[INFO] 第 {attempt} 次重试...");
            await Task.Delay(1000 * attempt);
        }

        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        var html = await response.Content.ReadAsStringAsync();

        // ── 提取页面标题 ──
        var title = ExtractTitle(html);

        // ── 提取正文 ──
        var content = format switch
        {
            "markdown" => HtmlToMarkdown(html),
            _ => HtmlToText(html)
        };

        // ── 截断 ──
        var truncated = false;
        if (content.Length > maxChars)
        {
            content = content[..maxChars];
            truncated = true;
        }

        // ── 输出 JSON ──
        var output = new FetchResult
        {
            Timestamp = timestamp,
            Url = url,
            Title = title,
            ContentType = contentType,
            ContentLength = html.Length,
            Format = format,
            Truncated = truncated,
            Content = content
        };

        var json = JsonSerializer.Serialize(output, JsonCtx.Default.FetchResult);
        Console.WriteLine(json);
        Environment.Exit(0);
        return;
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
        OutputError(url, $"HTTP 请求失败: {ex.StatusCode} {ex.Message}");
        Environment.Exit(2);
    }
    catch (TaskCanceledException)
    {
        OutputError(url, "请求超时 (20秒)");
        Environment.Exit(3);
    }
    catch (Exception ex)
    {
        OutputError(url, $"抓取失败: {ex.Message}");
        Environment.Exit(4);
    }
}

// ── HTML → 纯文本 ────────────────────────────────────────────────────────

static string HtmlToText(string html)
{
    if (string.IsNullOrWhiteSpace(html)) return "";

    var text = html;

    // 1. 移除无用标签
    text = Regex.Replace(text, @"<(script|style|noscript|svg)[^>]*>[\s\S]*?</\1>", "", RegexOptions.IgnoreCase);
    text = Regex.Replace(text, @"<(script|style|noscript|svg)[^>]*/>", "", RegexOptions.IgnoreCase);
    text = Regex.Replace(text, @"<link[^>]*>", "", RegexOptions.IgnoreCase);
    text = Regex.Replace(text, @"<meta[^>]*>", "", RegexOptions.IgnoreCase);
    text = Regex.Replace(text, @"<template[^>]*>[\s\S]*?</template>", "", RegexOptions.IgnoreCase);

    // 2. 移除 HTML 注释
    text = Regex.Replace(text, @"<!--[\s\S]*?-->", "", RegexOptions.IgnoreCase);

    // 3. 块级元素前插入换行
    text = Regex.Replace(text, @"<(br|hr)\s*/?>", "\n", RegexOptions.IgnoreCase);
    text = Regex.Replace(text, @"</(p|div|h[1-6]|li|tr|blockquote|pre|section|article|header|footer|main|nav)>", "\n", RegexOptions.IgnoreCase);
    text = Regex.Replace(text, @"<(p|div|h[1-6]|li|tr|blockquote|pre|section|article|header|footer|main|nav)[^>]*>", "\n", RegexOptions.IgnoreCase);

    // 4. 移除所有剩余 HTML 标签
    text = Regex.Replace(text, @"<[^>]+>", "");

    // 5. 解码 HTML 实体
    text = WebUtility.HtmlDecode(text);

    // 6. 清理空白
    text = Regex.Replace(text, @"[ \t]+", " ");
    text = Regex.Replace(text, @"\n[ \t]+", "\n");
    text = Regex.Replace(text, @"[ \t]+\n", "\n");
    text = Regex.Replace(text, @"\n{3,}", "\n\n");
    text = text.Trim();

    return text;
}

// ── HTML → Markdown ───────────────────────────────────────────────────────

static string HtmlToMarkdown(string html)
{
    if (string.IsNullOrWhiteSpace(html)) return "";

    var text = html;

    // 1. 移除无用标签
    text = Regex.Replace(text, @"<(script|style|noscript|svg)[^>]*>[\s\S]*?</\1>", "", RegexOptions.IgnoreCase);
    text = Regex.Replace(text, @"<(script|style|noscript|svg)[^>]*/>", "", RegexOptions.IgnoreCase);
    text = Regex.Replace(text, @"<link[^>]*>", "", RegexOptions.IgnoreCase);
    text = Regex.Replace(text, @"<meta[^>]*>", "", RegexOptions.IgnoreCase);
    text = Regex.Replace(text, @"<template[^>]*>[\s\S]*?</template>", "", RegexOptions.IgnoreCase);

    // 2. 移除 HTML 注释
    text = Regex.Replace(text, @"<!--[\s\S]*?-->", "", RegexOptions.IgnoreCase);

    // 3. 标题 → Markdown 标题
    for (int i = 6; i >= 1; i--)
        text = Regex.Replace(text, $@"<h{i}[^>]*>([\s\S]*?)</h{i}>", m => $"\n\n{'#'} {StripTags(m.Groups[1].Value)}\n\n", RegexOptions.IgnoreCase);

    // 4. 强调
    text = Regex.Replace(text, @"<(strong|b)[^>]*>([\s\S]*?)</\1>", m => $"**{StripTags(m.Groups[2].Value)}**", RegexOptions.IgnoreCase);
    text = Regex.Replace(text, @"<(em|i)[^>]*>([\s\S]*?)</\1>", m => $"*{StripTags(m.Groups[2].Value)}*", RegexOptions.IgnoreCase);

    // 5. 链接
    text = Regex.Replace(text, @"<a\s[^>]*href=""([^""]*)""[^>]*>([\s\S]*?)</a>",
        m => {
            var href = WebUtility.HtmlDecode(m.Groups[1].Value);
            var linkText = StripTags(m.Groups[2].Value).Trim();
            return string.IsNullOrWhiteSpace(linkText) ? "" : $"[{linkText}]({href})";
        }, RegexOptions.IgnoreCase);

    // 6. 代码块
    text = Regex.Replace(text, @"<pre[^>]*><code[^>]*>([\s\S]*?)</code></pre>",
        m => $"\n\n```\n{WebUtility.HtmlDecode(StripTags(m.Groups[1].Value))}\n```\n\n", RegexOptions.IgnoreCase);
    text = Regex.Replace(text, @"<pre[^>]*>([\s\S]*?)</pre>",
        m => $"\n\n```\n{WebUtility.HtmlDecode(StripTags(m.Groups[1].Value))}\n```\n\n", RegexOptions.IgnoreCase);

    // 7. 行内代码
    text = Regex.Replace(text, @"<code[^>]*>([\s\S]*?)</code>",
        m => $"`{WebUtility.HtmlDecode(StripTags(m.Groups[1].Value))}`", RegexOptions.IgnoreCase);

    // 8. 列表
    text = Regex.Replace(text, @"<li[^>]*>([\s\S]*?)</li>",
        m => $"- {StripTags(m.Groups[1].Value).Trim()}\n", RegexOptions.IgnoreCase);

    // 9. 块级元素换行
    text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
    text = Regex.Replace(text, @"</(p|div|blockquote|section|article|header|footer)>", "\n\n", RegexOptions.IgnoreCase);
    text = Regex.Replace(text, @"<(p|div|blockquote|section|article|header|footer)[^>]*>", "\n\n", RegexOptions.IgnoreCase);

    // 10. 移除剩余标签
    text = StripTags(text);

    // 11. 解码实体
    text = WebUtility.HtmlDecode(text);

    // 12. 清理空白
    text = Regex.Replace(text, @"[ \t]+", " ");
    text = Regex.Replace(text, @"\n[ \t]+", "\n");
    text = Regex.Replace(text, @"\n{3,}", "\n\n");
    text = text.Trim();

    return text;
}

// ── 工具方法 ──────────────────────────────────────────────────────────────

static string StripTags(string html)
{
    return Regex.Replace(html, @"<[^>]+>", "");
}

static string ExtractTitle(string html)
{
    var match = Regex.Match(html, @"<title[^>]*>([\s\S]*?)</title>", RegexOptions.IgnoreCase);
    return match.Success ? WebUtility.HtmlDecode(StripTags(match.Groups[1].Value)).Trim() : "";
}

static void OutputError(string url, string error)
{
    var output = new FetchResult
    {
        Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        Url = url,
        Error = error,
        Content = ""
    };
    var json = JsonSerializer.Serialize(output, JsonCtx.Default.FetchResult);
    Console.WriteLine(json);
}

// ── 数据模型 ──────────────────────────────────────────────────────────────

public class FetchResult
{
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("contentType")]
    public string ContentType { get; set; } = "";

    [JsonPropertyName("contentLength")]
    public int ContentLength { get; set; }

    [JsonPropertyName("format")]
    public string Format { get; set; } = "";

    [JsonPropertyName("truncated")]
    public bool Truncated { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(FetchResult))]
internal partial class JsonCtx : JsonSerializerContext { }
