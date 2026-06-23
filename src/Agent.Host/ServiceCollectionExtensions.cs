// ============================================================================
// 服务注册扩展 — 核心服务 DI 配置
// ============================================================================

using Agent.Core.Abstractions;
using Agent.Core.Coding;
using Agent.Core.Config;
using Agent.Core.Context;
using Agent.Core.Editor;
using Agent.Core.Engine;
using Agent.Core.Git;
using Agent.Core.LLM;
using Agent.Core.Planning;
using Agent.Core.Plugins;
using Agent.Core.SDK;
using Agent.Core.Security;
using Agent.Core.Tools;
using Microsoft.Extensions.Options;
using QuantumCore;
using PaperCore;

namespace Agent.Host;

/// <summary>
/// 核心服务 DI 注册扩展
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册所有核心服务 (安全、工具、LLM、SDK、Git、编辑器、任务引擎、插件 + QuantumCore + PaperCore)
    /// </summary>
    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        // ── 核心 ──
        services.AddSingleton<IToolRegistry, ToolRegistry>();
        services.AddSingleton<ToolExecutor>();

        // ── Phase 2: 安全子系统 ──
        services.AddSingleton<IRiskAssessor, RiskAssessor>();
        services.AddSingleton<IPolicyEngine, PolicyEngine>();
        services.AddSingleton<IReviewGate, ReviewGate>();
        services.AddSingleton<IAuditLogger, AuditLogger>();
        services.AddSingleton<CredentialHelper>();
        services.AddSingleton<ElevationAuditLogger>();

        // ── Phase 3: 工具体系 ──
        services.AddSingleton<PowerShellCommandTool>();
        services.AddSingleton<SaveMemoryTool>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<SaveMemoryTool>>();
            var config = sp.GetRequiredService<IOptions<AgentConfig>>().Value;
            var memoryPath = Path.Combine(config.Paths.DataDir, "config", "memory.md");
            return new SaveMemoryTool(logger, memoryPath);
        });

        // ── Phase 4: LLM & Agent Loop ──
        services.AddHttpClient<LlmConnector>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        });
        services.AddSingleton<SystemPromptBuilder>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<SystemPromptBuilder>>();
            var config = sp.GetRequiredService<IOptions<AgentConfig>>().Value;
            var configDir = Path.Combine(config.Paths.DataDir, "config");

            // basePrompt: 硬编码身份提示词（始终注入 Developer 角色）
            var basePrompt = Agent.Core.Config.DeveloperPrompts.BasePrompt;

            return new SystemPromptBuilder(logger, basePrompt, configDir: configDir);
        });
        services.AddSingleton<ContextSummarizer>();
        services.AddSingleton<AgentLoop>();

        // ── Phase 4.5: SDK 集成工具 (L0) ──
        services.AddSingleton<RuntimeCompiler>();
        services.AddSingleton<PackageManager>();
        services.AddSingleton<ProcessManager>();
        services.AddSingleton<CompileCodeTool>();
        services.AddSingleton<NuGetSearchTool>();
        services.AddSingleton<NuGetListTool>();
        services.AddSingleton<RunProcessTool>();
        services.AddSingleton<BrowserTool>(sp =>
        {
            var http = new HttpClient { BaseAddress = new Uri(AppConstants.DefaultBaseUrl), Timeout = TimeSpan.FromSeconds(120) };
            var logger = sp.GetRequiredService<ILogger<BrowserTool>>();
            return new BrowserTool(http, logger);
        });

        // 内置工具 (aria2c + 7z)
        services.AddSingleton<DownloaderTool>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<DownloaderTool>>();
            var toolsDir = Path.Combine(AppContext.BaseDirectory, "sdk", "tools");
            if (!File.Exists(Path.Combine(toolsDir, "aria2c.exe")))
                toolsDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Agent.Host", "sdk", "tools"));
            return new DownloaderTool(Path.Combine(toolsDir, "aria2c.exe"), logger);
        });
        services.AddSingleton<ArchiveTool>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<ArchiveTool>>();
            var toolsDir = Path.Combine(AppContext.BaseDirectory, "sdk", "tools");
            if (!File.Exists(Path.Combine(toolsDir, "7z.exe")))
                toolsDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Agent.Host", "sdk", "tools"));
            return new ArchiveTool(Path.Combine(toolsDir, "7z.exe"), logger);
        });

        // ── Phase 8: 编码调试工具链 ──
        services.AddSingleton<DotnetSdkManager>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<DotnetSdkManager>>();
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
            return new DotnetSdkManager(logger, httpClient);
        });
        services.AddSingleton<TestRunner>();
        services.AddSingleton<VsdbgDownloader>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<VsdbgDownloader>>();
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
            return new VsdbgDownloader(logger, httpClient);
        });
        services.AddTransient<VsDbgClient>();

        // ── Phase 5: Git 版本控制 ──
        services.AddSingleton<IGitService, GitService>();
        services.AddSingleton<GitAutoCommit>();

        // ── Phase 5.5: 会话存储 (使用 QuantumCore 混合引擎) ──
        services.AddSingleton<SessionStore>();

        // ── Phase 6: 文件编辑器 ──
        services.AddSingleton<TextFileEditor>();
        services.AddSingleton<EditorRouter>();

        // ── Phase 7: 任务引擎 ──
        services.AddSingleton<TaskQueue>();
        services.AddSingleton<TaskRunner>();
        services.AddHostedService(sp => sp.GetRequiredService<TaskRunner>());

        // ── Phase 8: 插件系统 ──
        services.AddSingleton<PluginLoader>();
        services.AddSingleton<PluginBuilder>();
        services.AddSingleton<CreatePluginTool>();
        services.AddSingleton<ListPluginsTool>();
        services.AddSingleton<ReloadPluginsTool>();
        services.AddSingleton<ManagePluginTool>();
        services.AddSingleton<ToolResultCache>();

        // ══════════════════════════════════════════════════
        //  Vega 重构: 三大开源组件集成
        // ══════════════════════════════════════════════════

        // ── QuantumCore: 嵌入式混合存储引擎 ──
        //    替代 JSON 文件存储，提供 Redis 风格 KV + WAL 崩溃恢复
        services.AddQuantumCoreStorage();

        // ── PaperCore: 知识库管理模块 ──
        //    Phase 13 RAG 基础，基于 Markdown 的知识存储
        services.AddPaperCoreKnowledge();

        return services;
    }
}
