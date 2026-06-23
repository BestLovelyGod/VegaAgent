// ============================================================================
// BuildErrorParser 单元测试
// ============================================================================

using Agent.Core.Coding;
using Microsoft.Extensions.Logging;
using Moq;

namespace Agent.Core.Tests;

public class BuildErrorParserTests
{
    private readonly BuildErrorParser _parser = new(Mock.Of<ILogger<BuildErrorParser>>());

    [Fact]
    public void Parse_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(_parser.Parse(""));
        Assert.Empty(_parser.Parse(null!));
    }

    [Fact]
    public void Parse_MsbuildError_ExtractsCorrectly()
    {
        var output = @"src\Program.cs(42,15): error CS1061: 'Foo' does not contain a definition for 'Bar'";

        var errors = _parser.Parse(output);

        Assert.Single(errors);
        Assert.Equal("src\\Program.cs", errors[0].FilePath);
        Assert.Equal(42, errors[0].Line);
        Assert.Equal(15, errors[0].Column);
        Assert.Equal("error", errors[0].Severity);
        Assert.Equal("CS1061", errors[0].Code);
        Assert.Contains("Foo", errors[0].Message);
    }

    [Fact]
    public void Parse_MsbuildWarning_ExtractsCorrectly()
    {
        var output = @"src\Utils.cs(10,1): warning CS0168: variable 'x' is declared but never used";

        var errors = _parser.Parse(output);

        Assert.Single(errors);
        Assert.Equal("warning", errors[0].Severity);
        Assert.Equal("CS0168", errors[0].Code);
    }

    [Fact]
    public void Parse_MultipleErrors_ExtractsAll()
    {
        var output = @"src\A.cs(1,1): error CS1001: error 1
src\B.cs(2,3): error CS1002: error 2
src\C.cs(3,5): warning CS1003: warning 1";

        var errors = _parser.Parse(output);

        Assert.Equal(3, errors.Length);
        Assert.Equal("CS1001", errors[0].Code);
        Assert.Equal("CS1002", errors[1].Code);
        Assert.Equal("CS1003", errors[2].Code);
    }

    [Fact]
    public void Parse_NoErrors_ReturnsEmpty()
    {
        var output = @"Build succeeded.
    0 Warning(s)
    0 Error(s)";

        var errors = _parser.Parse(output);
        Assert.Empty(errors);
    }
}
