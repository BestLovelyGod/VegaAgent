// ============================================================================
// 文件编辑器单元测试
// ============================================================================

using Agent.Core.Editor;
using Microsoft.Extensions.Logging;
using Moq;

namespace Agent.Core.Tests;

public class TextFileEditorTests : IDisposable
{
    private readonly TextFileEditor _editor;
    private readonly string _tempDir;

    public TextFileEditorTests()
    {
        _editor = new TextFileEditor(Mock.Of<ILogger<TextFileEditor>>());
        _tempDir = Path.Combine(Path.GetTempPath(), $"agent-editor-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task ReadWriteFile_WorksCorrectly()
    {
        var file = Path.Combine(_tempDir, "test.txt");
        await _editor.WriteFileAsync(file, "hello agent");
        var content = await _editor.ReadFileAsync(file);
        Assert.Equal("hello agent", content);
    }

    [Fact]
    public async Task ReplaceText_CountsCorrectly()
    {
        var file = Path.Combine(_tempDir, "replace.txt");
        await File.WriteAllTextAsync(file, "foo bar foo baz foo");
        var count = await _editor.ReplaceTextAsync(file, "foo", "qux");
        Assert.Equal(3, count);
        var content = await File.ReadAllTextAsync(file);
        Assert.Equal("qux bar qux baz qux", content);
    }

    [Fact]
    public async Task ReplaceText_NoMatch_ReturnsZero()
    {
        var file = Path.Combine(_tempDir, "nomatch.txt");
        await File.WriteAllTextAsync(file, "hello world");
        var count = await _editor.ReplaceTextAsync(file, "xyz", "abc");
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ReplaceRegex_WorksCorrectly()
    {
        var file = Path.Combine(_tempDir, "regex.txt");
        await File.WriteAllTextAsync(file, "line1\nline2\nline3");
        var count = await _editor.ReplaceRegexAsync(file, @"^line(\d+)", "item$1");
        Assert.Equal(3, count);
        var content = await File.ReadAllTextAsync(file);
        Assert.Equal("item1\nitem2\nitem3", content);
    }

    [Fact]
    public async Task InsertAtLine_WorksCorrectly()
    {
        var file = Path.Combine(_tempDir, "insert.txt");
        await File.WriteAllTextAsync(file, "line0\nline1\nline2");
        await _editor.InsertAtLineAsync(file, 1, "inserted");
        var lines = await File.ReadAllLinesAsync(file);
        Assert.Equal(4, lines.Length);
        Assert.Equal("inserted", lines[1]);
    }

    [Fact]
    public async Task DeleteLines_WorksCorrectly()
    {
        var file = Path.Combine(_tempDir, "delete.txt");
        await File.WriteAllTextAsync(file, "line0\nline1\nline2\nline3");
        await _editor.DeleteLinesAsync(file, 1, 2);
        var lines = await File.ReadAllLinesAsync(file);
        Assert.Equal(2, lines.Length);
        Assert.Equal("line0", lines[0]);
        Assert.Equal("line3", lines[1]);
    }

    [Fact]
    public async Task Append_WorksCorrectly()
    {
        var file = Path.Combine(_tempDir, "append.txt");
        await File.WriteAllTextAsync(file, "hello");
        await _editor.AppendAsync(file, " world");
        var content = await File.ReadAllTextAsync(file);
        Assert.Equal("hello world", content);
    }

    [Fact]
    public async Task BackupAndRestore_WorksCorrectly()
    {
        var file = Path.Combine(_tempDir, "backup.txt");
        await File.WriteAllTextAsync(file, "original");

        var backupPath = await _editor.CreateBackupAsync(file);
        Assert.True(File.Exists(backupPath));

        await File.WriteAllTextAsync(file, "modified");
        await _editor.RestoreBackupAsync(backupPath, file);

        var content = await File.ReadAllTextAsync(file);
        Assert.Equal("original", content);
    }

    [Fact]
    public async Task WriteFile_CreatesBackupBeforeOverwrite()
    {
        var file = Path.Combine(_tempDir, "overwrite.txt");
        await File.WriteAllTextAsync(file, "version 1");
        await _editor.WriteFileAsync(file, "version 2");
        Assert.Equal("version 2", await File.ReadAllTextAsync(file));
        Assert.True(File.Exists($"{file}.bak"));
        Assert.Equal("version 1", await File.ReadAllTextAsync($"{file}.bak"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}

public class EditorRouterTests
{
    [Theory]
    [InlineData("test.cs", true)]
    [InlineData("test.csproj", true)]
    [InlineData("test.sln", true)]
    [InlineData("test.ps1", false)]
    [InlineData("test.json", false)]
    public void SupportsSyntaxMode_CorrectClassification(string file, bool expected)
    {
        Assert.Equal(expected, EditorRouter.SupportsSyntaxMode(file));
    }
}
