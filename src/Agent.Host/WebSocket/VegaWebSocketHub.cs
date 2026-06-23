// ============================================================================
// Vega WebSocket Hub — 双向实时通信通道
// ============================================================================

using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Agent.Host.Ws;

/// <summary>
/// Vega WebSocket Hub — 管理所有 WebSocket 连接
/// <para>职责: 连接生命周期、心跳检测、消息广播、LLM/任务流式推送</para>
/// </summary>
public sealed class VegaWebSocketHub
{
    private readonly ConcurrentDictionary<string, WebSocketConnection> _connections = new();
    private readonly ILogger<VegaWebSocketHub> _logger;
    private readonly PeriodicTimer _heartbeatTimer;
    private CancellationTokenSource _cts = new();

    public VegaWebSocketHub(ILogger<VegaWebSocketHub> logger)
    {
        _logger = logger;
        _heartbeatTimer = new PeriodicTimer(TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// 当前连接数
    /// </summary>
    public int ConnectionCount => _connections.Count;

    /// <summary>查找已存在的连接（按 ClientInfo 匹配）</summary>
    public WebSocketConnection? FindByClientInfo(string clientInfo)
    {
        return _connections.Values.FirstOrDefault(c => c.ClientInfo == clientInfo);
    }

    /// <summary>
    /// 处理新的 WebSocket 连接
    /// </summary>
    public async Task HandleConnectionAsync(System.Net.WebSockets.WebSocket socket, string? clientInfo = null)
    {
        var connectionId = Guid.NewGuid().ToString("N")[..8];
        var connection = new WebSocketConnection
        {
            ConnectionId = connectionId,
            Socket = socket,
            ConnectedAt = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow,
            ClientInfo = clientInfo
        };

        _connections.TryAdd(connectionId, connection);
        _logger.LogInformation("[WS] 客户端已连接: {Id} ({Info})", connectionId, clientInfo ?? "unknown");

        // 发送连接确认
        await SendAsync(connection, new WsMessage
        {
            Type = WsMessageType.Ack,
            Data = new { connectionId, message = "connected" }
        });

        try
        {
            var buffer = new byte[8192];
            var segment = new ArraySegment<byte>(buffer);
            var messageBuffer = new StringBuilder();

            while (socket.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(segment, _cts.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("[WS] 客户端请求关闭: {Id}", connectionId);
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    // 累积分片消息
                    messageBuffer.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                    if (result.EndOfMessage)
                    {
                        connection.LastActivity = DateTime.UtcNow;
                        await HandleMessageAsync(connection, messageBuffer.ToString());
                        messageBuffer.Clear();
                    }
                }
            }
        }
        catch (WebSocketException ex)
        {
            _logger.LogWarning("[WS] 连接异常断开: {Id} - {Error}", connectionId, ex.Message);
        }
        catch (IOException ex)
        {
            _logger.LogWarning("[WS] 连接 IO 异常: {Id} - {Error}", connectionId, ex.Message);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _connections.TryRemove(connectionId, out var removed);
            removed?.Dispose();
            _logger.LogInformation("[WS] 客户端已断开: {Id}", connectionId);
        }
    }

    /// <summary>
    /// 处理收到的消息
    /// </summary>
    private async Task HandleMessageAsync(WebSocketConnection connection, string rawMessage)
    {
        try
        {
            var message = JsonSerializer.Deserialize<WsMessage>(rawMessage, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (message is null) return;

            switch (message.Type)
            {
                case WsMessageType.Pong:
                    // 心跳响应，无需处理
                    break;

                case WsMessageType.Ping:
                    await SendAsync(connection, new WsMessage
                    {
                        Type = WsMessageType.Pong,
                        RequestId = message.RequestId
                    });
                    break;

                case "llm.cancel":
                    // 客户端请求取消 LLM 流 — 广播取消通知
                    if (message.Data is JsonElement { ValueKind: JsonValueKind.Object } dataEl &&
                        dataEl.TryGetProperty("sessionId", out var sidProp))
                    {
                        var sid = sidProp.GetString();
                        if (sid is not null)
                        {
                            await PushLlmErrorAsync(sid, "用户取消");
                        }
                    }
                    break;

                default:
                    _logger.LogDebug("[WS] 收到消息: {Type} from {Id}", message.Type, connection.ConnectionId);
                    break;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("[WS] 消息解析失败: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// 向所有连接广播消息
    /// </summary>
    public async Task BroadcastAsync(WsMessage message)
    {
        var payload = JsonSerializer.Serialize(message, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        var bytes = Encoding.UTF8.GetBytes(payload);

        var tasks = _connections.Values.Select(async conn =>
        {
            try
            {
                if (conn.Socket.State == WebSocketState.Open)
                {
                    await conn.WriteLock.WaitAsync();
                    try
                    {
                        var segment = new ArraySegment<byte>(bytes);
                        await conn.Socket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                    finally
                    {
                        conn.WriteLock.Release();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("[WS] 广播失败: {Id} - {Error}", conn.ConnectionId, ex.Message);
            }
        });

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 向指定连接发送消息
    /// </summary>
    public async Task SendToAsync(string connectionId, WsMessage message)
    {
        if (!_connections.TryGetValue(connectionId, out var connection)) return;

        await SendAsync(connection, message);
    }

    /// <summary>
    /// 向所有 LLM 流订阅者推送数据块
    /// </summary>
    public async Task PushLlmChunkAsync(string sessionId, string? deltaContent, string? deltaReasoning = null)
    {
        if (string.IsNullOrEmpty(deltaContent) && string.IsNullOrEmpty(deltaReasoning)) return;

        var message = new WsMessage
        {
            Type = WsMessageType.LlmStream,
            Data = new
            {
                sessionId,
                delta = new
                {
                    content = deltaContent,
                    reasoning_content = deltaReasoning
                }
            }
        };

        await BroadcastAsync(message);
    }

    /// <summary>
    /// 推送 LLM 流结束
    /// </summary>
    public async Task PushLlmStreamEndAsync(string sessionId, string? finishReason = "stop")
    {
        var message = new WsMessage
        {
            Type = WsMessageType.LlmStreamEnd,
            Data = new { sessionId, finishReason }
        };

        await BroadcastAsync(message);
    }

    /// <summary>
    /// 推送 LLM 错误
    /// </summary>
    public async Task PushLlmErrorAsync(string sessionId, string error)
    {
        var message = new WsMessage
        {
            Type = WsMessageType.LlmError,
            Data = new { sessionId, error }
        };

        await BroadcastAsync(message);
    }

    /// <summary>
    /// 推送任务状态更新
    /// </summary>
    public async Task PushTaskUpdateAsync(string taskId, string status, object? data = null)
    {
        var message = new WsMessage
        {
            Type = WsMessageType.TaskUpdate,
            Data = new { taskId, status, data }
        };

        await BroadcastAsync(message);
    }

    /// <summary>
    /// 启动心跳检测
    /// </summary>
    public void StartHeartbeat()
    {
        _ = Task.Run(async () =>
        {
            while (await _heartbeatTimer.WaitForNextTickAsync(_cts.Token))
            {
                await SendHeartbeatAsync();
            }
        }, _cts.Token);
    }

    /// <summary>
    /// 发送心跳 ping
    /// </summary>
    private async Task SendHeartbeatAsync()
    {
        var now = DateTime.UtcNow;
        var staleConnections = _connections.Values
            .Where(c => (now - c.LastActivity).TotalMinutes > 2)
            .ToList();

        // 清理超时连接
        foreach (var conn in staleConnections)
        {
            _logger.LogWarning("[WS] 清理超时连接: {Id}", conn.ConnectionId);
            try
            {
                await conn.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "timeout", CancellationToken.None);
            }
            catch { }
            _connections.TryRemove(conn.ConnectionId, out _);
        }

        // 发送 ping
        if (_connections.Count > 0)
        {
            await BroadcastAsync(new WsMessage { Type = WsMessageType.Ping });
        }
    }

    /// <summary>
    /// 停止 Hub
    /// </summary>
    public async Task StopAsync()
    {
        _cts.Cancel();
        _heartbeatTimer.Dispose();

        // 关闭所有连接
        var tasks = _connections.Values.Select(async conn =>
        {
            try
            {
                if (conn.Socket.State == WebSocketState.Open)
                {
                    await conn.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "shutdown", CancellationToken.None);
                }
            }
            catch { }
        });

        await Task.WhenAll(tasks);

        // 释放所有连接资源
        foreach (var conn in _connections.Values)
            conn.Dispose();
        _connections.Clear();

        _cts.Dispose();
    }

    private static async Task SendAsync(WebSocketConnection connection, WsMessage message)
    {
        if (connection.Socket.State != WebSocketState.Open) return;

        var payload = JsonSerializer.Serialize(message, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        var bytes = Encoding.UTF8.GetBytes(payload);
        var segment = new ArraySegment<byte>(bytes);

        await connection.WriteLock.WaitAsync();
        try
        {
            await connection.Socket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        finally
        {
            connection.WriteLock.Release();
        }
    }

}
