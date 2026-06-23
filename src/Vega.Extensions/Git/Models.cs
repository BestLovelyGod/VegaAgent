// ============================================================================
// Git 数据模型
// ============================================================================

namespace Agent.Core.Git;

/// <summary>Git 提交</summary>
public sealed record GitCommit
{
    public string Sha { get; init; } = "";
    public string Message { get; init; } = "";
    public string Author { get; init; } = "";
    public DateTime Timestamp { get; init; }
    public string[]? Parents { get; init; }
}

/// <summary>Git 差异</summary>
public sealed record GitDiff
{
    public string? FilePath { get; init; }
    public string? OldContent { get; init; }
    public string? NewContent { get; init; }
    public GitDiffStatus Status { get; init; }
    public string[]? PatchLines { get; init; }
}

/// <summary>Git 差异状态</summary>
public enum GitDiffStatus
{
    Added,
    Modified,
    Deleted,
    Renamed
}

/// <summary>Git 分支</summary>
public sealed record GitBranch
{
    public string Name { get; init; } = "";
    public bool IsCurrent { get; init; }
    public string? TipSha { get; init; }
}

/// <summary>Git 仓库状态</summary>
public sealed record GitStatus
{
    public string[] Modified { get; init; } = [];
    public string[] Added { get; init; } = [];
    public string[] Deleted { get; init; } = [];
    public string[] Untracked { get; init; } = [];
    public bool IsClean => Modified.Length == 0 && Added.Length == 0 && Deleted.Length == 0 && Untracked.Length == 0;
}
