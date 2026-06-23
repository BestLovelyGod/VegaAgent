// ============================================================================
// Vega 扩展服务注册 — QuantumCore + PaperCore 集成
// ============================================================================

using Agent.Core.Config;
using Microsoft.Extensions.Options;
using QuantumCore;
using PaperCore;

namespace Agent.Host;

/// <summary>
/// Vega 扩展服务注册 — 三大开源组件集成
/// 
/// 注册:
///   - QuantumCore: 嵌入式混合存储引擎 (替代 JSON 文件存储)
///   - PaperCore: 知识库管理模块 (Phase 13 RAG 基础)
/// </summary>
public static class VegaExtensionsServiceCollection
{
    /// <summary>
    /// 注册 QuantumCore 混合存储引擎
    /// 数据目录: data/quantumcore/ (Bitcask + WAL)
    /// </summary>
    public static IServiceCollection AddQuantumCoreStorage(this IServiceCollection services)
    {
        services.AddSingleton<QuantumCoreOptions>(sp =>
        {
            var config = sp.GetRequiredService<IOptions<AgentConfig>>().Value;
            var dataDir = Path.Combine(config.Paths.DataDir, "quantumcore");
            return new QuantumCoreOptions
            {
                DataDirectory = dataDir,
                MaxMemoryBytes = 256 * 1024 * 1024, // 256MB
                MaxKeyCount = 100_000,
                WalEnabled = true,
                PersistIntervalSeconds = 60
            };
        });

        services.AddSingleton<IHybridStore>(sp =>
        {
            var options = sp.GetRequiredService<QuantumCoreOptions>();
            options.Validate();
            return new HybridStore(options);
        });

        return services;
    }

    /// <summary>
    /// 注册 PaperCore 知识库管理模块
    /// 知识目录: data/knowledge/ (Markdown 文件)
    /// </summary>
    public static IServiceCollection AddPaperCoreKnowledge(this IServiceCollection services)
    {
        services.AddSingleton<FileKnowledgeStore>(sp =>
        {
            var config = sp.GetRequiredService<IOptions<AgentConfig>>().Value;
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<FileKnowledgeStore>();
            return new FileKnowledgeStore(config.Paths.DataDir, logger);
        });

        services.AddSingleton<IKnowledgeProvider>(sp =>
        {
            var store = sp.GetRequiredService<FileKnowledgeStore>();
            var parsers = sp.GetServices<IDocumentParser>();
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<KnowledgeProvider>();
            return new KnowledgeProvider(store, parsers, logger);
        });

        // 注册内置文档解析器
        services.AddSingleton<IDocumentParser, PaperCore.Parsers.MarkdownParser>();

        return services;
    }
}
