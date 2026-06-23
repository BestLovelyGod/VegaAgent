// ============================================================================
// Git 自动提交中间件 — 每次编码操作自动快照
// ============================================================================

using Agent.Core.Models;
using Microsoft.Extensions.Logging;

namespace Agent.Core.Git;

/// <summary>
/// Git 自动提交中间件 — 拦截工具执行结果，自动提交修改的文件
/// 
/// 流程:
///   1. 工具执行完成且成功
///   2. 检查是否有修改的文件
///   3. 如果文件在 Git 仓库内 → 自动 commit
///   4. commit message: "[Agent] {工具名}: {摘要}"
/// </summary>
public sealed class GitAutoCommit
{
    private readonly IGitService _git;
    private readonly ILogger<GitAutoCommit> _logger;

    public GitAutoCommit(IGitService git, ILogger<GitAutoCommit> logger)
    {
        _git = git;
        _logger = logger;
    }

    /// <summary>
    /// 处理工具执行结果，自动提交修改的文件
    /// </summary>
    public async Task<ToolResult> OnAfterExecuteAsync(ToolResult result, CancellationToken ct = default)
    {
        // 只处理成功的、有修改文件的结果
        if (result.Status != ToolResultStatus.Success || result.ModifiedFiles is not { Length: > 0 })
            return result;

        try
        {
            // 查找最近的 Git 仓库
            var repoPath = await FindRepositoryRootAsync(result.ModifiedFiles[0]);
            if (repoPath is null)
            {
                _logger.LogDebug("修改的文件不在 Git 仓库中，跳过自动提交");
                return result;
            }

            var message = $"[Agent] {result.ToolName}: {result.Summary ?? "文件修改"}";
            await _git.AutoCommitAsync(repoPath, result.ModifiedFiles, message, ct);

            _logger.LogInformation("Git 自动提交完成: {Message}", message);
        }
        catch (Exception ex)
        {
            // 自动提交失败不应影响工具执行结果
            _logger.LogWarning(ex, "Git 自动提交失败，但工具执行结果不受影响");
        }

        return result;
    }

    /// <summary>
    /// 向上查找最近的 .git 目录
    /// </summary>
    private async Task<string?> FindRepositoryRootAsync(string filePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        var maxDepth = 20;
        var depth = 0;

        while (!string.IsNullOrEmpty(dir) && depth < maxDepth)
        {
            if (await _git.IsRepositoryAsync(dir))
                return dir;

            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
            depth++;
        }

        return null;
    }
}
