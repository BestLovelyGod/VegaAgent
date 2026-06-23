// RestClient.cs
// HTTP REST 客户端工具 — 支持 GET/POST/PUT/DELETE 请求

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

if (args.Length == 0)
{
    Console.WriteLine("用法: RestClient <method> <url> [body] [headers]");
    Console.WriteLine("  method: GET | POST | PUT | DELETE | PATCH");
    Console.WriteLine("  url: 请求地址");
    Console.WriteLine("  body: 请求体 (POST/PUT/PATCH, JSON 格式)");
    Console.WriteLine("  headers: 额外请求头 (JSON 格式, e.g. {\"Authorization\":\"Bearer xxx\"})");
    Console.WriteLine();
    Console.WriteLine("示例:");
    Console.WriteLine("  RestClient GET https://httpbin.org/get");
    Console.WriteLine("  RestClient POST https://httpbin.org/post {\\\"name\\\":\\\"test\\\"}");
    return;
}

var method = args[0].ToUpperInvariant();
var url = args[1];
var body = args.Length > 2 ? args[2] : null;
var headersJson = args.Length > 3 ? args[3] : null;

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

try
{
    var request = new HttpRequestMessage
    {
        Method = method switch
        {
            "GET" => HttpMethod.Get,
            "POST" => HttpMethod.Post,
            "PUT" => HttpMethod.Put,
            "DELETE" => HttpMethod.Delete,
            "PATCH" => HttpMethod.Patch,
            _ => throw new ArgumentException($"不支持的方法: {method}")
        },
        RequestUri = new Uri(url)
    };

    // 添加请求头
    if (!string.IsNullOrEmpty(headersJson))
    {
        var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson);
        if (headers is not null)
        {
            foreach (var kvp in headers)
            {
                request.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
            }
        }
    }

    // 添加请求体
    if (!string.IsNullOrEmpty(body) && method is "POST" or "PUT" or "PATCH")
    {
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
    }

    Console.WriteLine($"🔗 {method} {url}");
    Console.WriteLine();

    var response = await http.SendAsync(request);
    var responseBody = await response.Content.ReadAsStringAsync();

    Console.WriteLine($"状态码: {(int)response.StatusCode} {response.StatusCode}");
    Console.WriteLine($"耗时: {response.Headers.Date?.ToString("HH:mm:ss.fff") ?? "N/A"}");
    Console.WriteLine();

    // 响应头
    Console.WriteLine("响应头:");
    foreach (var header in response.Headers)
    {
        Console.WriteLine($"  {header.Key}: {string.Join(", ", header.Value)}");
    }
    foreach (var header in response.Content.Headers)
    {
        Console.WriteLine($"  {header.Key}: {string.Join(", ", header.Value)}");
    }
    Console.WriteLine();

    // 响应体
    Console.WriteLine("响应体:");
    try
    {
        // 尝试格式化 JSON
        var jsonDoc = JsonDocument.Parse(responseBody);
        Console.WriteLine(JsonSerializer.Serialize(jsonDoc, new JsonSerializerOptions { WriteIndented = true }));
    }
    catch
    {
        Console.WriteLine(responseBody);
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"请求失败: {ex.Message}");
}
