// ============================================================================
// CredentialHelper 单元测试 — 权限错误检测
// ============================================================================

using Agent.Core.Security;
using Microsoft.Extensions.Logging;
using Moq;

namespace Agent.Core.Tests;

public class CredentialHelperTests
{
    private readonly CredentialHelper _helper;

    public CredentialHelperTests()
    {
        _helper = new CredentialHelper(Mock.Of<ILogger<CredentialHelper>>());
    }

    // ─── IsPermissionError 纯函数测试 ──────────────────

    [Fact]
    public void IsPermissionError_Null_ReturnsFalse()
    {
        Assert.False(CredentialHelper.IsPermissionError(null));
    }

    [Fact]
    public void IsPermissionError_Empty_ReturnsFalse()
    {
        Assert.False(CredentialHelper.IsPermissionError(""));
    }

    [Fact]
    public void IsPermissionError_Whitespace_ReturnsFalse()
    {
        Assert.False(CredentialHelper.IsPermissionError("   "));
    }

    [Theory]
    [InlineData("Access Denied")]
    [InlineData("Permission Denied")]
    [InlineData("Access is denied")]
    [InlineData("Not Authorized")]
    [InlineData("Unauthorized")]
    [InlineData("Forbidden")]
    [InlineData("Requires elevation")]
    [InlineData("Run as administrator")]
    [InlineData("Administrator privilege required")]
    [InlineData("Elevated privilege needed")]
    public void IsPermissionError_EnglishKeywords_ReturnsTrue(string error)
    {
        Assert.True(CredentialHelper.IsPermissionError(error));
    }

    [Theory]
    [InlineData("权限不足")]
    [InlineData("拒绝访问")]
    [InlineData("需要管理员权限")]
    [InlineData("需要提升权限")]
    [InlineData("访问被拒绝")]
    [InlineData("没有权限")]
    [InlineData("无权限")]
    [InlineData("请求的操作需要提升")]
    public void IsPermissionError_ChineseKeywords_ReturnsTrue(string error)
    {
        Assert.True(CredentialHelper.IsPermissionError(error));
    }

    [Theory]
    [InlineData("File not found")]
    [InlineData("Connection timeout")]
    [InlineData("Syntax error")]
    [InlineData("成功完成")]
    public void IsPermissionError_NonPermissionErrors_ReturnsFalse(string error)
    {
        Assert.False(CredentialHelper.IsPermissionError(error));
    }

    [Fact]
    public void IsPermissionError_KeywordInSentence_ReturnsTrue()
    {
        Assert.True(CredentialHelper.IsPermissionError("Error: the operation requires elevation to complete"));
        Assert.True(CredentialHelper.IsPermissionError("命令执行失败: 请求的操作需要提升"));
    }

    [Fact]
    public void IsPermissionError_CaseInsensitive()
    {
        Assert.True(CredentialHelper.IsPermissionError("ACCESS DENIED"));
        Assert.True(CredentialHelper.IsPermissionError("access denied"));
        Assert.True(CredentialHelper.IsPermissionError("Access Denied"));
    }

    // ─── 常量验证 ─────────────────────────────────────

    [Fact]
    public void ElevatedUserName_IsIVega()
    {
        Assert.Equal("IVega", CredentialHelper.ElevatedUserName);
    }

    // ─── GetElevationHint 不崩溃 ──────────────────────

    [Fact]
    public void GetElevationHint_ReturnsNonEmptyString()
    {
        var hint = _helper.GetElevationHint();
        Assert.NotNull(hint);
        Assert.NotEmpty(hint);
    }
}
