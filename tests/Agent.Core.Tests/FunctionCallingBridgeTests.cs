// ============================================================================
// FunctionCallingBridge 单元测试
// ============================================================================

using Agent.Core.Models;
using Agent.Core.Planning;

namespace Agent.Core.Tests;

public class FunctionCallingBridgeTests
{
    [Fact]
    public void ConvertToolToSchema_GeneratesCorrectSchema()
    {
        var tool = new ToolInfo
        {
            Name = "Get-Process",
            Description = "获取进程列表",
            Category = ToolCategory.PowerShellScript,
            Parameters =
            [
                new() { Name = "Name", Type = "string", Description = "进程名", Required = true },
                new() { Name = "Top", Type = "number", Description = "数量", Required = false, Default = 10 }
            ]
        };

        var schema = FunctionCallingBridge.ConvertToolToSchema(tool);

        Assert.Equal("function", schema.Type);
        Assert.Equal("Get-Process", schema.Function.Name);
        Assert.Equal("获取进程列表", schema.Function.Description);
    }

    [Fact]
    public void ConvertToolCallToRequest_ParsesArguments()
    {
        var toolCall = new LlmToolCall
        {
            Id = "call_123",
            Type = "function",
            Function = new LlmFunctionCall
            {
                Name = "Get-SystemInfo",
                Arguments = "{\"IncludeDisks\":true,\"Top\":5}"
            }
        };

        var request = FunctionCallingBridge.ConvertToolCallToRequest(toolCall, "test-session");

        Assert.Equal("Get-SystemInfo", request.ToolName);
        Assert.Equal("test-session", request.SessionId);
        Assert.True((bool)request.Parameters["IncludeDisks"]);
        Assert.Equal(5.0, (double)request.Parameters["Top"]); // JSON numbers are double
    }

    [Fact]
    public void ConvertResultToMessage_Success_IncludesOutput()
    {
        var result = new ToolResult
        {
            RequestId = "req_123",
            Status = ToolResultStatus.Success,
            Output = "CPU: 23%",
            ToolName = "Get-SystemInfo"
        };

        var message = FunctionCallingBridge.ConvertResultToMessage(result, "call_123");

        Assert.Equal("tool", message.Role);
        Assert.Equal("call_123", message.ToolCallId);
        Assert.Equal("Get-SystemInfo", message.Name);
        Assert.Equal("CPU: 23%", message.Content);
    }

    [Fact]
    public void ConvertResultToMessage_Failure_IncludesError()
    {
        var result = new ToolResult
        {
            RequestId = "req_123",
            Status = ToolResultStatus.Failed,
            Error = "超时",
            Output = "部分输出",
            ToolName = "powershell"
        };

        var message = FunctionCallingBridge.ConvertResultToMessage(result, "call_123");

        Assert.Contains("[错误]", message.Content);
        Assert.Contains("超时", message.Content);
    }

    [Fact]
    public void ConvertToolsToSchema_MultipleTools_ReturnsAll()
    {
        var tools = new List<ToolInfo>
        {
            new() { Name = "Tool1", Description = "A", Parameters = [] },
            new() { Name = "Tool2", Description = "B", Parameters = [] }
        };

        var schemas = FunctionCallingBridge.ConvertToolsToSchema(tools);

        Assert.Equal(2, schemas.Length);
        Assert.Equal("Tool1", schemas[0].Function.Name);
        Assert.Equal("Tool2", schemas[1].Function.Name);
    }
}
