// ============================================================================
// Vega WebSocket Client — 双向实时通信客户端
// ============================================================================

class VegaWebSocket {
    constructor(url) {
        // 动态构建 WebSocket URL：基于当前页面 host，端口固定 7300
        this.url = url || `ws://${window.location.hostname || 'localhost'}:7300/ws`;
        this.socket = null;
        this.connectionId = null;
        this.isConnected = false;
        this.reconnectAttempts = 0;
        this.maxReconnectAttempts = 10;
        this.reconnectDelay = 1000; // 初始延迟 1 秒
        this.handlers = new Map();
        this.heartbeatTimer = null;
        this.reconnectTimer = null;
        
        // 绑定方法
        this._onOpen = this._onOpen.bind(this);
        this._onMessage = this._onMessage.bind(this);
        this._onClose = this._onClose.bind(this);
        this._onError = this._onError.bind(this);
    }

    /**
     * 连接到 WebSocket 服务器
     */
    connect() {
        if (this.socket && (this.socket.readyState === WebSocket.OPEN || this.socket.readyState === WebSocket.CONNECTING)) {
            console.log('[WS] 已连接或正在连接中');
            return;
        }

        console.log('[WS] 正在连接:', this.url);
        
        try {
            this.socket = new WebSocket(this.url);
            this.socket.onopen = this._onOpen;
            this.socket.onmessage = this._onMessage;
            this.socket.onclose = this._onClose;
            this.socket.onerror = this._onError;
        } catch (error) {
            console.error('[WS] 连接失败:', error);
            this._scheduleReconnect();
        }
    }

    /**
     * 断开连接
     */
    disconnect() {
        this.maxReconnectAttempts = 0; // 阻止自动重连
        
        if (this.heartbeatTimer) {
            clearInterval(this.heartbeatTimer);
            this.heartbeatTimer = null;
        }
        
        if (this.reconnectTimer) {
            clearTimeout(this.reconnectTimer);
            this.reconnectTimer = null;
        }
        
        if (this.socket) {
            this.socket.close(1000, 'client disconnect');
            this.socket = null;
        }
        
        this.isConnected = false;
        this.connectionId = null;
    }

    /**
     * 发送消息
     */
    send(type, data = null, requestId = null) {
        if (!this.socket || this.socket.readyState !== WebSocket.OPEN) {
            console.warn('[WS] 未连接，无法发送消息');
            return false;
        }

        const message = {
            type,
            data,
            requestId,
            timestamp: Date.now()
        };

        try {
            this.socket.send(JSON.stringify(message));
            return true;
        } catch (error) {
            console.error('[WS] 发送失败:', error);
            return false;
        }
    }

    /**
     * 注册消息处理器
     */
    on(type, handler) {
        if (!this.handlers.has(type)) {
            this.handlers.set(type, []);
        }
        this.handlers.get(type).push(handler);
        
        // 返回取消注册函数
        return () => {
            const handlers = this.handlers.get(type);
            if (handlers) {
                const index = handlers.indexOf(handler);
                if (index > -1) {
                    handlers.splice(index, 1);
                }
            }
        };
    }

    /**
     * 移除消息处理器
     */
    off(type, handler) {
        const handlers = this.handlers.get(type);
        if (handlers) {
            const index = handlers.indexOf(handler);
            if (index > -1) {
                handlers.splice(index, 1);
            }
        }
    }

    /**
     * 连接成功回调
     */
    _onOpen(event) {
        console.log('[WS] 连接成功');
        this.isConnected = true;
        this.reconnectAttempts = 0;
        this.reconnectDelay = 1000;
        
        // 启动心跳
        this._startHeartbeat();
        
        // 触发连接成功事件
        this._emit('connected', { timestamp: Date.now() });
    }

    /**
     * 收到消息回调
     */
    _onMessage(event) {
        try {
            const message = JSON.parse(event.data);
            this._handleMessage(message);
        } catch (error) {
            console.error('[WS] 消息解析失败:', error);
        }
    }

    /**
     * 处理收到的消息
     */
    _handleMessage(message) {
        const { type, data } = message;
        
        // 心跳响应
        if (type === 'pong') {
            return;
        }
        
        // 连接确认
        if (type === 'ack' && data?.connectionId) {
            this.connectionId = data.connectionId;
            console.log('[WS] Connection ID:', this.connectionId);
        }
        
        // 触发对应类型的处理器
        this._emit(type, data, message);
    }

    /**
     * 连接关闭回调
     */
    _onClose(event) {
        console.log('[WS] 连接关闭:', event.code, event.reason);
        this.isConnected = false;
        this.connectionId = null;
        
        if (this.heartbeatTimer) {
            clearInterval(this.heartbeatTimer);
            this.heartbeatTimer = null;
        }
        
        // 触发断开连接事件
        this._emit('disconnected', { code: event.code, reason: event.reason });
        
        // 尝试重连
        if (event.code !== 1000) { // 非正常关闭
            this._scheduleReconnect();
        }
    }

    /**
     * 连接错误回调
     */
    _onError(event) {
        console.error('[WS] 连接错误:', event);
        this._emit('error', { error: event });
    }

    /**
     * 启动心跳
     */
    _startHeartbeat() {
        if (this.heartbeatTimer) {
            clearInterval(this.heartbeatTimer);
        }
        
        this.heartbeatTimer = setInterval(() => {
            if (this.isConnected) {
                this.send('ping');
            }
        }, 30000); // 每 30 秒发送一次心跳
    }

    /**
     * 安排重连
     */
    _scheduleReconnect() {
        if (this.reconnectAttempts >= this.maxReconnectAttempts) {
            console.log('[WS] 已达到最大重连次数，停止重连');
            this._emit('reconnect_failed', { attempts: this.reconnectAttempts });
            return;
        }
        
        this.reconnectAttempts++;
        const delay = Math.min(this.reconnectDelay * Math.pow(1.5, this.reconnectAttempts - 1), 30000);
        
        console.log(`[WS] ${delay}ms 后重连 (第 ${this.reconnectAttempts} 次)`);
        
        this.reconnectTimer = setTimeout(() => {
            this.connect();
        }, delay);
    }

    /**
     * 触发事件
     */
    _emit(type, data, rawMessage = null) {
        const handlers = this.handlers.get(type);
        if (handlers) {
            handlers.forEach(handler => {
                try {
                    handler(data, rawMessage);
                } catch (error) {
                    console.error('[WS] Handler error:', error);
                }
            });
        }
    }
}

// 全局单例
window.vegaWs = new VegaWebSocket();
