// ============================================================================
// Git 服务接口
// ============================================================================

namespace Agent.Core.Git;

/// <summary>
/// Git 服务接口 — 纯 .NET Git 实现 (不依赖外部 git.exe)
/// </summary>
public interface IGitService
{
    // 仓库操作
    Task<bool> IsRepositoryAsync(string path);
    Task InitRepositoryAsync(string path);
    Task CloneAsync(string url, string localPath, CancellationToken ct = default);

    // 提交操作
    Task<GitCommit> CommitAsync(string repoPath, string message, CancellationToken ct = default);
    Task<GitCommit> AutoCommitAsync(string repoPath, string[] files, string action, CancellationToken ct = default);

    // 分支操作
    Task<GitBranch[]> GetBranchesAsync(string repoPath);
    Task CreateBranchAsync(string repoPath, string branchName);
    Task CheckoutAsync(string repoPath, string branchName);

    // 差异操作
    Task<GitDiff[]> GetDiffAsync(string repoPath, string? filePath = null);

    // 历史操作
    Task<GitCommit[]> GetLogAsync(string repoPath, int count = 20);
    Task<GitCommit> GetLastCommitAsync(string repoPath);

    // 回滚操作
    Task ResetToCommitAsync(string repoPath, string commitSha);

    // 状态
    Task<GitStatus> GetStatusAsync(string repoPath);
}
