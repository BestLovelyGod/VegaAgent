// ============================================================================
// Agent.Host — 主机进程入口
// ============================================================================

using Agent.Core.Config;
using Agent.Host;
using Agent.Host.Endpoints;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// ─────────────────────────────────────────────────────────
// 0. 配置加载
// ─────────────────────────────────────────────────────────
builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

var configuredPort = builder.Configuration.GetValue<int?>("Agent:Service:Port") ?? AppConstants.DefaultPort;
builder.WebHost.UseUrls($"http://localhost:{configuredPort}");

// 用户配置文件 (data/config.json)
var dataConfigPaths = new[]
{
    Path.Combine(AppContext.BaseDirectory, "..", "data", "config.json"),
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "config.json"),
    Path.Combine(Directory.GetCurrentDirectory(), "data", "config.json"),
    Path.Combine(AppContext.BaseDirectory, "data", "config.json"),
};
var userConfigPath = dataConfigPaths.Select(p => Path.GetFullPath(p)).FirstOrDefault(File.Exists);
if (userConfigPath is not null)
{
    builder.Configuration.AddJsonFile(userConfigPath, optional: true, reloadOnChange: true);
    Log.Information("已加载用户配置: {Path}", userConfigPath);
}
else
{
    Log.Warning("未找到 data/config.json (已检查: {Paths})", string.Join(", ", dataConfigPaths));
}

builder.Configuration.AddEnvironmentVariables(prefix: "AGENT_");

// IVega 提权账户检查
{
    var (ivegaAvailable, ivegaHint) = Agent.Core.Security.CredentialHelper.CheckAvailability();
    if (!ivegaAvailable)
        Log.Information("IVega 账户不可用 (可选): {Hint}", ivegaHint.Split('\n').FirstOrDefault()?.Trim());
    else
        Log.Information("IVega 提权账户可用");
}

// ─────────────────────────────────────────────────────────
// 1. Serilog 日志
// ─────────────────────────────────────────────────────────
builder.Host.UseSerilog((context, services, config) =>
{
    var logConfig = config
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "IgnorantVega");

    logConfig.WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");

    var logsDir = Path.Combine(AppContext.BaseDirectory, "..", "data", "logs");
    if (!Directory.Exists(logsDir))
        logsDir = Path.Combine(AppContext.BaseDirectory, "data", "logs");
    Directory.CreateDirectory(logsDir);
    logConfig.WriteTo.File(
        path: Path.Combine(logsDir, "agent-.json"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        fileSizeLimitBytes: 50 * 1024 * 1024,
        formatter: new Serilog.Formatting.Compact.CompactJsonFormatter());
});

// ─────────────────────────────────────────────────────────
// 2. 配置绑定 + 服务注册
// ─────────────────────────────────────────────────────────
builder.Services.Configure<AgentConfig>(
    builder.Configuration.GetSection(AgentConfig.SectionName));

builder.Services.AddCoreServices();

// API 服务
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "Ignorant Vega API",
        Version = "v1",
        Description = "Ignorant Vega — Windows 设备管理 · 织女星"
    });
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.SetIsOriginAllowed(origin =>
                origin.StartsWith("http://localhost:") ||
                origin.StartsWith("https://localhost:") ||
                origin.StartsWith("http://127.0.0.1:") ||
                origin.StartsWith("https://127.0.0.1:"))
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// ─────────────────────────────────────────────────────────
// 3. 中间件管道
// ─────────────────────────────────────────────────────────
app.UseCors();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ─────────────────────────────────────────────────────────
// 4. 启动检查 + 工具注册 + 端点映射 + 插件加载
// ─────────────────────────────────────────────────────────
{
    var agentConfig = app.Services.GetRequiredService<IOptions<AgentConfig>>().Value;
    var apiKey = ApiKeyProvider.Resolve(agentConfig.Llm.ApiKey);

    if (string.IsNullOrWhiteSpace(apiKey))
    {
        Log.Warning("⚠️ LLM API Key 未配置！Agent Loop 功能将不可用。");
        Log.Warning("   方式1: 编辑 data/config.json 设置 Agent:Llm:ApiKey");
        Log.Warning("   方式2: 设置环境变量 $env:AGENT_API_KEY = \"your-api-key\"");
    }
    else
    {
        Log.Information("LLM API Key 已配置 ({Prefix}...)", apiKey.Length > 8 ? apiKey[..8] : "***");
    }
}

app.RegisterTools();

app.MapHealthEndpoints();
app.MapTaskEndpoints();
app.MapConfigEndpoints();
app.MapToolEndpoints();
app.MapAuditEndpoints();
app.MapOpenAiEndpoints();
app.MapPluginEndpoints();
app.MapPromptEndpoints();
app.MapBrowserEndpoints();
app.MapSessionEndpoints();
app.MapLlmConfigEndpoints();

// ── Vega 重构新增端点: PaperCore 知识库 ──
{
    var knowledgeProvider = app.Services.GetRequiredService<PaperCore.IKnowledgeProvider>();
    var knowledgeGroup = app.MapGroup("/api/knowledge").WithTags("Knowledge").WithDescription("知识库管理");

    knowledgeGroup.MapGet("/search", async (string q, int max = 10) =>
    {
        var entries = await knowledgeProvider.SearchAsync(q, max);
        return Results.Ok(entries);
    }).WithName("SearchKnowledge").WithDescription("搜索知识库");

    knowledgeGroup.MapGet("/", async () =>
    {
        var entries = await knowledgeProvider.GetAllAsync();
        return Results.Ok(new { total = entries.Count, entries });
    }).WithName("GetAllKnowledge").WithDescription("获取所有知识条目");

    knowledgeGroup.MapGet("/{id}", async (string id) =>
    {
        var entry = await knowledgeProvider.GetAsync(id);
        return entry is not null ? Results.Ok(entry) : Results.NotFound();
    }).WithName("GetKnowledge").WithDescription("获取单条知识");

    knowledgeGroup.MapPost("/", async (PaperCore.KnowledgeEntry entry) =>
    {
        var result = await knowledgeProvider.AddAsync(entry);
        return Results.Ok(result);
    }).WithName("AddKnowledge").WithDescription("添加知识条目");

    knowledgeGroup.MapGet("/categories", async () =>
    {
        var categories = await knowledgeProvider.GetCategoriesAsync();
        return Results.Ok(categories);
    }).WithName("GetKnowledgeCategories").WithDescription("获取知识分类列表");

    Log.Information("PaperCore 知识库端点已注册 (/api/knowledge)");
}

await app.LoadPluginsAsync();

// ─────────────────────────────────────────────────────────
// 5. 启动
// ─────────────────────────────────────────────────────────
app.Lifetime.ApplicationStarted.Register(() =>
{
    Log.Information("════════════════════════════════════════════════");
    Log.Information("  Ignorant Vega 已启动");
    var baseUrl = app.Urls.FirstOrDefault() ?? AppConstants.DefaultBaseUrl;
    Log.Information("  地址: {Url}", baseUrl);
    Log.Information("  Swagger: {Url}/swagger", baseUrl);
    Log.Information("════════════════════════════════════════════════");
});

app.Run();
