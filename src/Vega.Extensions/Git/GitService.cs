// ============================================================================
// Git 服务实现 — LibGit2Sharp (纯 .NET Git，零外部依赖)
// ============================================================================

using LibGit2Sharp;
using Microsoft.Extensions.Logging;

namespace Agent.Core.Git;

/// <summary>
/// Git 服务实现 — 使用 LibGit2Sharp 提供完整 Git 能力
/// 
/// 核心原则:
///   - 不依赖外部 git.exe
///   - commit message 统一前缀 [Agent]
///   - 不自动 push (除非用户明确要求)
///   - 保留完整回滚链
/// </summary>
public sealed class GitService : IGitService
{
    private readonly ILogger<GitService> _logger;

    public GitService(ILogger<GitService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<bool> IsRepositoryAsync(string path)
    {
        return Task.FromResult(Repository.IsValid(path));
    }

    /// <inheritdoc/>
    public Task InitRepositoryAsync(string path)
    {
        Repository.Init(path);
        _logger.LogInformation("Git 仓库已初始化: {Path}", path);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task CloneAsync(string url, string localPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await Task.Run(() => Repository.Clone(url, localPath), ct);
        _logger.LogInformation("Git 仓库已克隆: {Url} → {Path}", url, localPath);
    }

    /// <inheritdoc/>
    public Task<GitCommit> CommitAsync(string repoPath, string message, CancellationToken ct = default)
    {
        using var repo = new Repository(repoPath);

        ct.ThrowIfCancellationRequested();

        // Stage 所有变更
        Commands.Stage(repo, "*");

        var signature = GetSignature(repo);
        try
        {
            var commit = repo.Commit(message, signature, signature);
            _logger.LogInformation("Git 提交: {Sha} — {Message}", commit.Sha[..Math.Min(8, commit.Sha.Length)], message);
            return Task.FromResult(MapCommit(commit));
        }
        catch (EmptyCommitException)
        {
            _logger.LogDebug("无变更，跳过提交: {Message}", message);
            var existing = repo.Head?.Tip;
            return Task.FromResult(existing is not null ? MapCommit(existing) : new GitCommit { Sha = "none", Message = "无提交" });
        }
    }

    /// <inheritdoc/>
    public Task<GitCommit> AutoCommitAsync(string repoPath, string[] files, string action, CancellationToken ct = default)
    {
        using var repo = new Repository(repoPath);

        ct.ThrowIfCancellationRequested();

        // Stage 指定文件 (验证路径在仓库内)
        foreach (var file in files)
        {
            var fullPath = Path.GetFullPath(file);
            if (!fullPath.StartsWith(Path.GetFullPath(repoPath), StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("文件不在仓库内，跳过: {File}", file);
                continue;
            }
            var relativePath = Path.GetRelativePath(repoPath, file);
            Commands.Stage(repo, relativePath);
        }

        // Stage 后检查是否有暂存变更
        var status = repo.RetrieveStatus();
        if (!status.Staged.Any())
        {
            _logger.LogDebug("无变更，跳过自动提交: {Action}", action);
            var lastCommit = repo.Head.Tip;
            return Task.FromResult(lastCommit is not null ? MapCommit(lastCommit) : new GitCommit { Sha = "none", Message = "无提交" });
        }

        var signature = GetSignature(repo);
        var commit = repo.Commit($"[Agent] {action}", signature, signature);

        _logger.LogInformation("Git 自动提交: {Sha} — [Agent] {Action}", commit.Sha[..Math.Min(8, commit.Sha.Length)], action);

        return Task.FromResult(MapCommit(commit));
    }

    /// <inheritdoc/>
    public Task<GitBranch[]> GetBranchesAsync(string repoPath)
    {
        using var repo = new Repository(repoPath);
        var branches = repo.Branches
            .Where(b => !b.IsRemote)
            .Select(b => new GitBranch
            {
                Name = b.FriendlyName,
                IsCurrent = b.IsCurrentRepositoryHead,
                TipSha = b.Tip?.Sha
            })
            .ToArray();

        return Task.FromResult(branches);
    }

    /// <inheritdoc/>
    public Task CreateBranchAsync(string repoPath, string branchName)
    {
        using var repo = new Repository(repoPath);
        repo.CreateBranch(branchName);
        _logger.LogInformation("Git 分支已创建: {Branch}", branchName);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task CheckoutAsync(string repoPath, string branchName)
    {
        using var repo = new Repository(repoPath);
        Commands.Checkout(repo, branchName);
        _logger.LogInformation("Git 切换分支: {Branch}", branchName);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<GitDiff[]> GetDiffAsync(string repoPath, string? filePath = null)
    {
        using var repo = new Repository(repoPath);

        // 空仓库无 diff
        if (repo.Head.Tip is null)
            return Task.FromResult(Array.Empty<GitDiff>());

        var changes = repo.Diff.Compare<TreeChanges>(repo.Head.Tip.Tree, DiffTargets.Index | DiffTargets.WorkingDirectory);

        var diffs = changes.Select(c => new GitDiff
        {
            FilePath = c.Path,
            Status = c.Status switch
            {
                ChangeKind.Added => GitDiffStatus.Added,
                ChangeKind.Modified => GitDiffStatus.Modified,
                ChangeKind.Deleted => GitDiffStatus.Deleted,
                ChangeKind.Renamed => GitDiffStatus.Renamed,
                _ => GitDiffStatus.Modified
            }
        }).ToArray();

        if (filePath is not null)
            diffs = diffs.Where(d => d.FilePath == filePath).ToArray();

        return Task.FromResult(diffs);
    }

    /// <inheritdoc/>
    public Task<GitCommit[]> GetLogAsync(string repoPath, int count = 20)
    {
        using var repo = new Repository(repoPath);
        var commits = repo.Commits
            .Take(count)
            .Select(MapCommit)
            .ToArray();

        return Task.FromResult(commits);
    }

    /// <inheritdoc/>
    public Task<GitCommit> GetLastCommitAsync(string repoPath)
    {
        using var repo = new Repository(repoPath);
        var commit = repo.Head.Tip
            ?? throw new InvalidOperationException("仓库没有任何提交");

        return Task.FromResult(MapCommit(commit));
    }

    /// <inheritdoc/>
    public Task ResetToCommitAsync(string repoPath, string commitSha)
    {
        using var repo = new Repository(repoPath);

        // 验证 commit SHA 存在
        var commit = repo.Lookup<Commit>(commitSha)
            ?? throw new ArgumentException($"无效的 commit SHA: {commitSha}");

        repo.Reset(ResetMode.Hard, commit);
        var displaySha = commitSha.Length >= 8 ? commitSha[..8] : commitSha;
        _logger.LogWarning("Git 已回滚到: {Sha}", displaySha);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<GitStatus> GetStatusAsync(string repoPath)
    {
        using var repo = new Repository(repoPath);
        var status = repo.RetrieveStatus();

        return Task.FromResult(new GitStatus
        {
            Modified = status.Modified.Select(f => f.FilePath).ToArray(),
            Added = status.Staged.Where(s => s.State == FileStatus.NewInIndex).Select(f => f.FilePath).ToArray(),
            Deleted = status.Missing.Select(f => f.FilePath).ToArray(),
            Untracked = status.Untracked.Select(f => f.FilePath).ToArray()
        });
    }

    private static Signature GetSignature(Repository repo)
    {
        // 使用仓库配置的用户信息，或默认值
        var name = repo.Config.Get<string>("user.name")?.Value ?? "Ignorant Vega";
        var email = repo.Config.Get<string>("user.email")?.Value ?? "agent@localhost";
        return new Signature(name, email, DateTimeOffset.Now);
    }

    private static GitCommit MapCommit(LibGit2Sharp.Commit commit) => new()
    {
        Sha = commit.Sha,
        Message = commit.MessageShort,
        Author = commit.Author.Name,
        Timestamp = commit.Author.When.DateTime,
        Parents = commit.Parents.Select(p => p.Sha).ToArray()
    };
}
