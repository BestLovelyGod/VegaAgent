// ============================================================================
// Git 服务单元测试
// ============================================================================

using Agent.Core.Git;
using Microsoft.Extensions.Logging;
using Moq;

namespace Agent.Core.Tests;

public class GitServiceTests : IDisposable
{
    private readonly GitService _git;
    private readonly string _tempDir;

    public GitServiceTests()
    {
        _git = new GitService(Mock.Of<ILogger<GitService>>());
        _tempDir = Path.Combine(Path.GetTempPath(), $"agent-git-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task InitRepository_CreatesValidRepo()
    {
        await _git.InitRepositoryAsync(_tempDir);
        var isRepo = await _git.IsRepositoryAsync(_tempDir);
        Assert.True(isRepo);
    }

    [Fact]
    public async Task IsRepository_NonRepo_ReturnsFalse()
    {
        var nonRepoDir = Path.Combine(_tempDir, "not-a-repo");
        Directory.CreateDirectory(nonRepoDir);
        var isRepo = await _git.IsRepositoryAsync(nonRepoDir);
        Assert.False(isRepo);
    }

    [Fact]
    public async Task CommitAndLog_WorksCorrectly()
    {
        // Init
        await _git.InitRepositoryAsync(_tempDir);

        // 创建一个文件
        var testFile = Path.Combine(_tempDir, "test.txt");
        await File.WriteAllTextAsync(testFile, "hello agent");

        // 提交
        var commit = await _git.CommitAsync(_tempDir, "[Agent] 测试提交");

        Assert.NotEmpty(commit.Sha);
        Assert.Equal("[Agent] 测试提交", commit.Message);
        Assert.NotEmpty(commit.Author); // 使用系统 git 配置的用户名

        // 查看日志
        var log = await _git.GetLogAsync(_tempDir);
        Assert.Single(log);
        Assert.Equal(commit.Sha, log[0].Sha);
    }

    [Fact]
    public async Task AutoCommit_WithFiles_CommitsCorrectly()
    {
        await _git.InitRepositoryAsync(_tempDir);

        // 创建文件并初始提交
        var file1 = Path.Combine(_tempDir, "file1.txt");
        await File.WriteAllTextAsync(file1, "initial");
        await _git.CommitAsync(_tempDir, "[Agent] 初始提交");

        // 修改文件
        await File.WriteAllTextAsync(file1, "modified content");
        var file2 = Path.Combine(_tempDir, "file2.txt");
        await File.WriteAllTextAsync(file2, "new file");

        // 自动提交
        var commit = await _git.AutoCommitAsync(_tempDir, [file1, file2], "修改文件");

        Assert.NotEmpty(commit.Sha);
        Assert.Contains("[Agent]", commit.Message);
    }

    [Fact]
    public async Task GetStatus_ReportsChanges()
    {
        await _git.InitRepositoryAsync(_tempDir);

        // 创建并提交一个文件
        var testFile = Path.Combine(_tempDir, "tracked.txt");
        await File.WriteAllTextAsync(testFile, "initial");
        await _git.CommitAsync(_tempDir, "[Agent] 初始提交");

        // 修改文件
        await File.WriteAllTextAsync(testFile, "modified");

        // 添加未跟踪文件
        var untracked = Path.Combine(_tempDir, "untracked.txt");
        await File.WriteAllTextAsync(untracked, "new");

        var status = await _git.GetStatusAsync(_tempDir);

        Assert.Contains("tracked.txt", status.Modified);
        Assert.Contains("untracked.txt", status.Untracked);
        Assert.False(status.IsClean);
    }

    [Fact]
    public async Task BranchOperations_WorkCorrectly()
    {
        await _git.InitRepositoryAsync(_tempDir);

        // 初始提交
        var testFile = Path.Combine(_tempDir, "test.txt");
        await File.WriteAllTextAsync(testFile, "initial");
        await _git.CommitAsync(_tempDir, "[Agent] 初始提交");

        // 获取当前分支名
        var branches = await _git.GetBranchesAsync(_tempDir);
        var currentBranch = branches.FirstOrDefault(b => b.IsCurrent);
        Assert.NotNull(currentBranch);

        // 创建分支
        await _git.CreateBranchAsync(_tempDir, "feature");

        // 切换分支
        await _git.CheckoutAsync(_tempDir, "feature");

        // 验证当前分支
        branches = await _git.GetBranchesAsync(_tempDir);
        var current = branches.FirstOrDefault(b => b.IsCurrent);
        Assert.NotNull(current);
        Assert.Equal("feature", current.Name);
    }

    [Fact]
    public async Task ResetToCommit_WorksCorrectly()
    {
        await _git.InitRepositoryAsync(_tempDir);

        // 第一次提交
        var testFile = Path.Combine(_tempDir, "test.txt");
        await File.WriteAllTextAsync(testFile, "version 1");
        var commit1 = await _git.CommitAsync(_tempDir, "[Agent] 版本 1");
        Assert.NotEqual("none", commit1.Sha);

        // 第二次提交
        await File.WriteAllTextAsync(testFile, "version 2");
        var commit2 = await _git.CommitAsync(_tempDir, "[Agent] 版本 2");
        Assert.NotEqual("none", commit2.Sha);

        // 回滚到第一次提交
        await _git.ResetToCommitAsync(_tempDir, commit1.Sha);

        // 验证文件内容已回滚
        var content = await File.ReadAllTextAsync(testFile);
        Assert.Equal("version 1", content);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
