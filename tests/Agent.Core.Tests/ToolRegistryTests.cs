// ============================================================================
// ToolRegistry 单元测试
// ============================================================================

using Agent.Core.Abstractions;
using Agent.Core.Models;
using Agent.Core.Tools;
using Microsoft.Extensions.Logging;
using Moq;

namespace Agent.Core.Tests;

public class ToolRegistryTests
{
    private readonly ToolRegistry _registry;
    private readonly Mock<ILogger<ToolRegistry>> _loggerMock = new();

    public ToolRegistryTests()
    {
        _registry = new ToolRegistry(_loggerMock.Object);
    }

    [Fact]
    public void Register_ToolCanBeRetrieved()
    {
        // Arrange
        var tool = CreateMockTool("TestTool", "A test tool");

        // Act
        _registry.Register(tool.Object);

        // Assert
        Assert.True(_registry.Contains("TestTool"));
        var retrieved = _registry.GetTool("TestTool");
        Assert.NotNull(retrieved);
        Assert.Equal("TestTool", retrieved.Name);
    }

    [Fact]
    public void Register_CaseInsensitive()
    {
        // Arrange
        var tool = CreateMockTool("TestTool", "A test tool");

        // Act
        _registry.Register(tool.Object);

        // Assert
        Assert.True(_registry.Contains("testtool"));
        Assert.True(_registry.Contains("TESTTOOL"));
    }

    [Fact]
    public void Unregister_RemovesTool()
    {
        // Arrange
        var tool = CreateMockTool("TestTool", "A test tool");
        _registry.Register(tool.Object);

        // Act
        var result = _registry.Unregister("TestTool");

        // Assert
        Assert.True(result);
        Assert.False(_registry.Contains("TestTool"));
        Assert.Null(_registry.GetTool("TestTool"));
    }

    [Fact]
    public void Unregister_NonExistent_ReturnsFalse()
    {
        var result = _registry.Unregister("NonExistent");
        Assert.False(result);
    }

    [Fact]
    public void GetAllTools_ReturnsAllRegistered()
    {
        // Arrange
        _registry.Register(CreateMockTool("ToolA", "Tool A").Object);
        _registry.Register(CreateMockTool("ToolB", "Tool B").Object);
        _registry.Register(CreateMockTool("ToolC", "Tool C").Object);

        // Act
        var tools = _registry.GetAllTools();

        // Assert
        Assert.Equal(3, tools.Count);
        Assert.Contains(tools, t => t.Name == "ToolA");
        Assert.Contains(tools, t => t.Name == "ToolB");
        Assert.Contains(tools, t => t.Name == "ToolC");
    }

    [Fact]
    public void GetAllTools_EmptyRegistry_ReturnsEmpty()
    {
        var tools = _registry.GetAllTools();
        Assert.Empty(tools);
    }

    [Fact]
    public void Register_DuplicateName_OverwritesExisting()
    {
        // Arrange
        var tool1 = CreateMockTool("TestTool", "Version 1");
        var tool2 = CreateMockTool("TestTool", "Version 2");

        // Act
        _registry.Register(tool1.Object);
        _registry.Register(tool2.Object);

        // Assert
        var retrieved = _registry.GetTool("TestTool");
        Assert.NotNull(retrieved);
        Assert.Equal("Version 2", retrieved.Description);
    }

    private static Mock<ITool> CreateMockTool(string name, string description)
    {
        var mock = new Mock<ITool>();
        mock.Setup(t => t.Name).Returns(name);
        mock.Setup(t => t.Description).Returns(description);
        mock.Setup(t => t.Category).Returns(ToolCategory.PowerShellScript);
        mock.Setup(t => t.RiskLevel).Returns(RiskLevel.Level0);
        mock.Setup(t => t.GetParameters()).Returns([]);
        mock.Setup(t => t.GetInfo()).Returns(() => new ToolInfo
        {
            Name = name,
            Description = description,
            Category = ToolCategory.PowerShellScript,
            RiskLevel = RiskLevel.Level0,
            Parameters = []
        });
        return mock;
    }
}
