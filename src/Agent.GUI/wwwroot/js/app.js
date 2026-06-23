// ============================================================================
// 无感 · 织女星 — 前端应用逻辑
// ============================================================================

const API_BASE = 'http://localhost:7300';
const LOCAL_API = 'http://localhost:5100'; // GUI 本地服务
const API_TIMEOUT = 8000; // API 请求超时 (毫秒)

// 带超时的 fetch，防止核心启动中请求挂死
async function fetchApi(url, options = {}, timeout = API_TIMEOUT) {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), timeout);
    try {
        const res = await fetch(url, { ...options, signal: controller.signal });
        clearTimeout(timer);
        return res;
    } catch (e) {
        clearTimeout(timer);
        if (e.name === 'AbortError') throw new Error('请求超时');
        throw e;
    }
}

// ============================================================================
// 状态管理
// ============================================================================

let state = {
    activeView: 'chat',
    activeSessionId: null,
    sessions: [],
    llmConfig: null,
    coreStatus: 'stopped',
    editingConfig: null,
    networkOnline: true,
    selectedHistory: new Set(),
};

// 任务取消控制
let currentAbortController = null;
let isTaskRunning = false;

const COLLAPSE_THRESHOLD = 500;

const PROVIDER_CN = {
    'mimo': '小米 API',
    'mimo-token-plan': '小米订阅 API',
    'deepseek': '深度求索 API',
};

// ============================================================================
// 初始化
// ============================================================================

document.addEventListener('DOMContentLoaded', async () => {
    await loadSessions();
    await loadLlmConfig();
    setupInputHandlers();
    startHostStatusMonitor();
});

// ============================================================================
// 会话管理
// ============================================================================

async function loadSessions() {
    try {
        const res = await fetchApi(`${API_BASE}/api/sessions`);
        if (res.ok) {
            state.sessions = await res.json();
            renderSessionList();
        }
    } catch (e) {
        console.error('加载会话失败:', e);
    }
}

function renderSessionList() {
    const container = document.getElementById('session-list');
    if (!state.sessions || state.sessions.length === 0) {
        container.innerHTML = '<div style="padding: 8px 16px; color: var(--text-secondary); font-size: 12px;">暂无对话</div>';
        return;
    }
    
    const sorted = [...state.sessions].sort((a, b) => {
        if (a.pinned !== b.pinned) return b.pinned ? 1 : -1;
        return new Date(b.updatedAt) - new Date(a.updatedAt);
    });
    
    container.innerHTML = sorted.map(s => `
        <div class="session-item ${s.id === state.activeSessionId ? 'active' : ''}" 
             onclick="selectSession('${s.id}')"
             oncontextmenu="showSessionMenu(event, '${s.id}')">
            <span class="session-title">${s.pinned ? '📌 ' : ''}${escapeHtml(s.title)}</span>
            <span class="session-time">${formatTime(s.updatedAt)}</span>
        </div>
    `).join('');
}

async function createNewSession() {
    try {
        const res = await fetchApi(`${API_BASE}/api/sessions`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ title: '新对话' })
        });
        if (res.ok) {
            const data = await res.json();
            await loadSessions();
            selectSession(data.sessionId);
        }
    } catch (e) {
        console.error('创建会话失败:', e);
    }
}

async function selectSession(id) {
    state.activeSessionId = id;
    state.activeView = 'chat';
    renderSessionList();
    showView('chat');
    await loadMessages(id);
}

async function loadMessages(sessionId) {
    try {
        const res = await fetchApi(`${API_BASE}/api/sessions/${sessionId}/messages`);
        if (res.ok) {
            const messages = await res.json();
            renderMessages(messages);
        }
    } catch (e) {
        console.error('加载消息失败:', e);
    }
}

function renderMessages(messages) {
    const container = document.getElementById('chat-messages');
    if (!messages || messages.length === 0) {
        container.innerHTML = `
            <div class="empty-state">
                <div class="empty-state-icon">💬</div>
                <div class="empty-state-text">开始对话吧</div>
            </div>`;
        return;
    }
    
    container.innerHTML = messages.map((m, i) => {
        const isLong = m.content && m.content.length > COLLAPSE_THRESHOLD;
        const display = isLong ? m.content.substring(0, COLLAPSE_THRESHOLD) + '...' : m.content;
        return `
            <div class="message-bubble ${m.role}" data-full-content="${escapeAttr(m.content)}">
                <div class="content" id="content-msg-${i}">${escapeHtml(display)}</div>
                ${isLong ? `<button class="btn-collapse" id="btn-msg-${i}" onclick="toggleCollapse(${i})">展开全文</button>` : ''}
                <div class="timestamp">${formatTime(m.timestamp)}</div>
            </div>
        `;
    }).join('');
    
    container.scrollTop = container.scrollHeight;
}

function toggleCollapse(index) {
    const bubble = document.querySelectorAll('.message-bubble')[index];
    if (!bubble) return;
    const contentEl = document.getElementById(`content-msg-${index}`);
    const btnEl = document.getElementById(`btn-msg-${index}`);
    if (!contentEl || !btnEl) return;
    
    const full = bubble.dataset.fullContent;
    if (btnEl.textContent === '展开全文') {
        contentEl.textContent = full;
        btnEl.textContent = '收起';
    } else {
        contentEl.textContent = full.substring(0, COLLAPSE_THRESHOLD) + '...';
        btnEl.textContent = '展开全文';
    }
}

async function deleteSession(id) {
    if (!confirm('确定删除此对话？删除后不可恢复')) return;
    
    try {
        const res = await fetchApi(`${API_BASE}/api/sessions/${id}`, { method: 'DELETE' });
        if (res.ok) {
            if (state.activeSessionId === id) {
                state.activeSessionId = null;
            }
            await loadSessions();
        }
    } catch (e) {
        console.error('删除会话失败:', e);
    }
}

// ============================================================================
// 消息发送
// ============================================================================

function setTaskRunning(running) {
    isTaskRunning = running;
    const btnSend = document.getElementById('btn-send');
    const btnCancel = document.getElementById('btn-cancel');
    if (running) {
        btnSend.style.display = 'none';
        btnCancel.style.display = 'inline-flex';
    } else {
        btnSend.style.display = 'inline-flex';
        btnCancel.style.display = 'none';
        updateSendButton();
    }
}

function cancelTask() {
    if (currentAbortController) {
        currentAbortController.abort();
        currentAbortController = null;
    }
    setTaskRunning(false);
    appendMessage('system', '⚠️ 已取消发送', true);
}

async function sendMessage() {
    const input = document.getElementById('chat-input');
    const content = input.value.trim();
    if (!content) return;

    // 没有活跃会话时自动创建
    if (!state.activeSessionId) {
        try {
            const res = await fetchApi(`${API_BASE}/api/sessions`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ title: content.substring(0, 30) })
            });
            if (res.ok) {
                const data = await res.json();
                state.activeSessionId = data.sessionId;
                await loadSessions();
            } else {
                appendMessage('system', '创建会话失败，请重试', true);
                return;
            }
        } catch (e) {
            appendMessage('system', '创建会话失败: ' + e.message, true);
            return;
        }
    }

    input.value = '';
    updateSendButton();
    
    // 显示用户消息
    appendMessage('user', content);
    
    // 检查核心状态
    if (state.coreStatus !== 'running') {
        appendMessage('system', '核心服务未启动，请先启动核心', true);
        return;
    }

    // 保存到会话
    try {
        await fetchApi(`${API_BASE}/api/sessions/${state.activeSessionId}/messages`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ role: 'user', content })
        });
        
        setTaskRunning(true);
        currentAbortController = new AbortController();
        const response = await callLlmApiStream(content);
        setTaskRunning(false);
        currentAbortController = null;
        if (response) {
            await fetchApi(`${API_BASE}/api/sessions/${state.activeSessionId}/messages`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ role: 'assistant', content: response })
            });
        } else if (!isTaskRunning) {
            // 用户取消时不显示超时提示
        } else {
            appendMessage('system', '发送超时，点击重试', true);
        }
    } catch (e) {
        console.error('发送消息失败:', e);
        appendMessage('system', '发送失败，请重试', true);
    }
}

// 流式调用 LLM API，实时更新 UI
async function callLlmApiStream(content) {
    const bubble = appendMessage('assistant', '', false, true); // 创建空的 assistant 气泡，返回 DOM 元素
    const contentEl = bubble.querySelector('.content');
    let fullText = '';
    
    try {
        const res = await fetch(`${API_BASE}/v1/chat/completions`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ messages: [{ role: 'user', content }], stream: true }),
            signal: currentAbortController?.signal
        });
        
        if (!res.ok) {
            contentEl.textContent = '请求失败: ' + res.status;
            return null;
        }
        
        const reader = res.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';
        
        while (true) {
            const { done, value } = await reader.read();
            if (done) break;
            
            buffer += decoder.decode(value, { stream: true });
            const lines = buffer.split('\n');
            buffer = lines.pop(); // 保留不完整的行
            
            for (const line of lines) {
                if (!line.startsWith('data: ')) continue;
                const data = line.slice(6).trim();
                if (data === '[DONE]') break;
                
                try {
                    const chunk = JSON.parse(data);
                    const delta = chunk.choices?.[0]?.delta;
                    if (delta?.content) {
                        fullText += delta.content;
                        contentEl.textContent = fullText;
                        // 自动滚动到底部
                        const container = document.getElementById('chat-messages');
                        container.scrollTop = container.scrollHeight;
                    }
                } catch {}
            }
        }
        
        // 更新 data-full-content 用于展开/收起
        bubble.dataset.fullContent = fullText;
        return fullText || null;
    } catch (e) {
        console.error('LLM 流式调用失败:', e);
        contentEl.textContent = fullText || '请求失败: ' + e.message;
        return fullText || null;
    }
}

// 非流式调用 (备用)
async function callLlmApi(content) {
    try {
        const controller = new AbortController();
        const timeout = setTimeout(() => controller.abort(), 120000);
        
        const res = await fetchApi(`${API_BASE}/v1/chat/completions`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ messages: [{ role: 'user', content }], stream: false }),
            signal: controller.signal
        });
        clearTimeout(timeout);
        
        if (res.ok) {
            const data = await res.json();
            return data.choices?.[0]?.message?.content || '无响应';
        }
    } catch (e) {
        console.error('LLM API 调用失败:', e.name === 'AbortError' ? '超时' : e.message);
    }
    return null;
}

async function callLlmApiWithRetry(content, maxRetries = 2) {
    for (let i = 0; i <= maxRetries; i++) {
        const result = await callLlmApi(content);
        if (result) return result;
        if (i < maxRetries) await new Promise(r => setTimeout(r, 1000 * (i + 1)));
    }
    return null;
}

async function retryLastMessage(btnEl) {
    const bubble = btnEl.closest('.message-bubble');
    if (!bubble) return;
    
    // 找到前一条用户消息
    const prevBubble = bubble.previousElementSibling;
    const userContent = prevBubble?.dataset?.fullContent || '';
    
    btnEl.disabled = true;
    btnEl.textContent = '重试中...';
    
    bubble.remove();
    
    const response = await callLlmApiStream(userContent);
    
    if (response) {
        if (state.activeSessionId) {
            await fetchApi(`${API_BASE}/api/sessions/${state.activeSessionId}/messages`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ role: 'assistant', content: response })
            });
        }
    } else {
        appendMessage('system', '重试失败，请稍后再试', true);
    }
}

function appendMessage(role, content, retryable = false, streaming = false) {
    const container = document.getElementById('chat-messages');
    const emptyState = container.querySelector('.empty-state');
    if (emptyState) emptyState.remove();
    
    const index = container.querySelectorAll('.message-bubble').length;
    const isLong = content && content.length > COLLAPSE_THRESHOLD;
    const display = isLong ? content.substring(0, COLLAPSE_THRESHOLD) + '...' : content;
    
    const div = document.createElement('div');
    div.className = `message-bubble ${role}`;
    div.dataset.fullContent = content;
    div.innerHTML = `
        <div class="content" id="content-msg-${index}">${escapeHtml(display)}</div>
        ${isLong ? `<button class="btn-collapse" id="btn-msg-${index}" onclick="toggleCollapse(${index})">展开全文</button>` : ''}
        ${retryable ? `<button class="btn-retry" onclick="retryLastMessage(this)">🔄 重试</button>` : ''}
        <div class="timestamp">${formatTime(new Date().toISOString())}</div>
    `;
    container.appendChild(div);
    container.scrollTop = container.scrollHeight;
    return div;
}

// ============================================================================
// 视图切换
// ============================================================================

async function switchView(view) {
    state.activeView = view;
    showView(view);
    
    // 更新侧边栏高亮
    document.querySelectorAll('.nav-item').forEach(item => {
        item.classList.toggle('active', item.dataset.view === view);
    });
    
    // 加载配置编辑器内容
    if (['agent', 'memory', 'user'].includes(view)) {
        loadConfigEditor(view);
        setTimeout(setupScrollSync, 100);
    }
    if (view === 'settings') {
        await loadLlmConfig();
        renderSettings();
    }
    if (view === 'history') {
        loadHistory();
    }
}

function showView(view) {
    const views = ['chat', 'editor', 'settings', 'history'];
    views.forEach(v => {
        const el = document.getElementById(`view-${v}`);
        if (el) el.style.display = v === view ? '' : 'none';
    });
    
    // 编辑器视图特殊处理
    if (['agent', 'memory', 'user'].includes(view)) {
        document.getElementById('view-editor').style.display = '';
        document.getElementById('view-chat').style.display = 'none';
    }
}

// ============================================================================
// 配置编辑器
// ============================================================================

async function loadConfigEditor(type) {
    state.editingConfig = type;
    try {
        const res = await fetch(`${LOCAL_API}/api/prompt/${type}`);
        if (res.ok) {
            const data = await res.json();
            document.getElementById('editor-textarea').value = data.content || '';
            updatePreview();
        }
    } catch (e) {
        console.error('加载配置失败:', e);
        document.getElementById('editor-textarea').value = `# 加载失败\n\n无法加载 ${type}.md`;
        updatePreview();
    }
}

let saveTimeout = null;
let previewSyncing = false;

function updatePreview() {
    const content = document.getElementById('editor-textarea').value;
    document.getElementById('preview-content').innerHTML = simpleMarkdown(content);
    
    if (saveTimeout) clearTimeout(saveTimeout);
    saveTimeout = setTimeout(() => saveConfig(), 2000);
}

// 编辑器滚动同步
function setupScrollSync() {
    const textarea = document.getElementById('editor-textarea');
    const preview = document.getElementById('preview-content');
    if (!textarea || !preview) return;
    
    textarea.addEventListener('scroll', () => {
        if (previewSyncing) return;
        previewSyncing = true;
        const ratio = textarea.scrollTop / (textarea.scrollHeight - textarea.clientHeight || 1);
        preview.parentElement.scrollTop = ratio * (preview.parentElement.scrollHeight - preview.parentElement.clientHeight);
        setTimeout(() => previewSyncing = false, 50);
    });
}

async function saveConfig() {
    if (!state.editingConfig) return;
    const content = document.getElementById('editor-textarea').value;
    
    try {
        await fetch(`${LOCAL_API}/api/prompt/${state.editingConfig}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'text/plain' },
            body: content
        });
        showSaveStatus('✓ 已保存');
    } catch (e) {
        showSaveStatus('✗ 保存失败');
    }
}

function showSaveStatus(text) {
    const el = document.getElementById('save-status');
    el.textContent = text;
    setTimeout(() => el.textContent = '', 2000);
}

// ============================================================================
// 设置页面
// ============================================================================

async function loadLlmConfig() {
    try {
        const res = await fetch(`${LOCAL_API}/api/config/llm`);
        if (res.ok) {
            state.llmConfig = await res.json();
        }
    } catch (e) {
        console.error('加载 LLM 配置失败:', e);
    }
}

function renderSettings() {
    if (!state.llmConfig) return;
    
    const config = state.llmConfig;
    
    // 设置当前提供商
    document.getElementById('provider-select').value = config.activeProvider;
    
    // 渲染提供商配置
    const providerConfigs = document.getElementById('provider-configs');
    providerConfigs.innerHTML = Object.entries(config.providers).map(([id, p]) => `
        <div style="background: var(--bg-tertiary); border-radius: 8px; padding: 16px; margin-top: 12px;">
            <div style="font-weight: 600; margin-bottom: 12px;">${escapeHtml(p.name)} <span style="color: var(--text-secondary); font-weight: 400;">(${PROVIDER_CN[id] || ''})</span></div>
            <div class="settings-row">
                <span class="settings-label">API 地址:</span>
                <input type="text" class="settings-input" value="${escapeHtml(p.baseUrl)}" readonly>
            </div>
            <div class="settings-row">
                <span class="settings-label">API Key:</span>
                <input type="password" class="settings-input" id="apikey-${id}" value="" placeholder="${p.apiKey || '未设置'}">
                <button class="btn-action" onclick="toggleApiKeyVisibility('${id}')">👁</button>
                <button class="btn-action" onclick="testConnection('${id}')">测试</button>
            </div>
            <div class="settings-row">
                <span class="settings-label"></span>
                <button class="btn-action btn-primary" onclick="updateApiKey('${id}')">保存 Key</button>
            </div>
        </div>
    `).join('');
    
    // 设置模型选择
    const modelSelect = document.getElementById('model-select');
    const currentProvider = config.providers[config.activeProvider];
    if (currentProvider) {
        modelSelect.innerHTML = currentProvider.models.map(m => 
            `<option value="${m}" ${m === config.activeModel ? 'selected' : ''}>${m}</option>`
        ).join('');
    }
}

async function switchProvider() {
    const provider = document.getElementById('provider-select').value;
    try {
        await fetch(`${LOCAL_API}/api/config/llm/provider`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ provider })
        });
        await loadLlmConfig();
        renderSettings();
    } catch (e) {
        console.error('切换提供商失败:', e);
    }
}

async function switchModel() {
    const model = document.getElementById('model-select').value;
    try {
        await fetch(`${LOCAL_API}/api/config/llm/model`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ model })
        });
        await loadLlmConfig();
        renderSettings();
    } catch (e) {
        console.error('切换模型失败:', e);
    }
}

async function updateApiKey(providerId) {
    const input = document.getElementById(`apikey-${providerId}`);
    const btn = input?.closest('.settings-row')?.querySelector('.btn-primary');
    const apiKey = input?.value;
    
    if (!apiKey) {
        showToast('请先输入 API Key', 'warning');
        input?.focus();
        return;
    }
    
    if (btn) { btn.textContent = '保存中...'; btn.disabled = true; }
    try {
        const res = await fetch(`${LOCAL_API}/api/config/llm/provider/${providerId}/apikey`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ apiKey })
        });
        if (res.ok) {
            input.value = '';
            input.placeholder = '已更新';
            showToast('✓ API Key 已更新', 'success');
        } else {
            const data = await res.json().catch(() => ({}));
            showToast('✗ 保存失败: ' + (data.error || res.statusText), 'error');
        }
    } catch (e) {
        showToast('✗ 保存失败: ' + e.message, 'error');
        console.error('更新 API Key 失败:', e);
    } finally {
        if (btn) { btn.textContent = '保存 Key'; btn.disabled = false; }
    }
}

async function testConnection(providerId) {
    if (state.coreStatus !== 'running') {
        showToast('请先启动核心服务', 'warning');
        return;
    }
    // 找到对应的测试按钮并显示 loading
    const input = document.getElementById(`apikey-${providerId}`);
    const settingsRow = input?.closest('.settings-row');
    const testBtn = settingsRow?.querySelector('button[onclick*="testConnection"]');
    
    if (testBtn) { testBtn.textContent = '⏳ 测试中...'; testBtn.disabled = true; }
    try {
        const res = await fetchApi(`${API_BASE}/api/config/llm/provider/${providerId}/test`, {
            method: 'POST'
        }, 15000);
        const data = await res.json();
        if (data.status === 'ok') {
            showToast(`✓ 连接成功！延迟: ${data.latency}ms — ${data.provider || ''}`, 'success');
        } else {
            showToast(`✗ 连接失败: ${data.error}`, 'error');
        }
    } catch (e) {
        showToast('✗ 测试失败: ' + e.message + ' — 请确认核心服务已启动', 'error');
    } finally {
        if (testBtn) { testBtn.textContent = '测试'; testBtn.disabled = false; }
    }
}

function toggleApiKeyVisibility(providerId) {
    const input = document.getElementById(`apikey-${providerId}`);
    input.type = input.type === 'password' ? 'text' : 'password';
}

// ============================================================================
// 历史记录
// ============================================================================

async function loadHistory() {
    await loadSessions();
    renderHistory(state.sessions);
}

function renderHistory(sessions) {
    const container = document.getElementById('history-list');
    if (!sessions || sessions.length === 0) {
        container.innerHTML = '<div style="color: var(--text-secondary);">暂无历史记录</div>';
        return;
    }
    
    container.innerHTML = sessions.map(s => `
        <div class="history-item">
            <input type="checkbox" class="history-checkbox" 
                   ${state.selectedHistory.has(s.id) ? 'checked' : ''}
                   onchange="toggleHistorySelect('${s.id}', this.checked)">
            <div class="history-item-content" onclick="selectSession('${s.id}')">
                <div style="font-weight: 600;">${s.pinned ? '📌 ' : ''}${escapeHtml(s.title)}</div>
                <div style="font-size: 12px; color: var(--text-secondary); margin-top: 4px;">
                    ${formatTime(s.updatedAt)} · ${s.messageCount} 条消息
                </div>
            </div>
        </div>
    `).join('');
}

function searchHistory() {
    const keyword = document.getElementById('history-search').value.toLowerCase();
    const filtered = state.sessions.filter(s => s.title.toLowerCase().includes(keyword));
    renderHistory(filtered);
}

function toggleHistorySelect(id, checked) {
    checked ? state.selectedHistory.add(id) : state.selectedHistory.delete(id);
    updateBatchButtons();
}

function toggleSelectAllHistory() {
    const allChecked = state.selectedHistory.size === state.sessions.length;
    state.selectedHistory.clear();
    if (!allChecked) state.sessions.forEach(s => state.selectedHistory.add(s.id));
    document.querySelectorAll('.history-checkbox').forEach(cb => cb.checked = !allChecked);
    updateBatchButtons();
}

function updateBatchButtons() {
    const btn = document.getElementById('btn-batch-delete');
    if (btn) {
        const n = state.selectedHistory.size;
        btn.disabled = n === 0;
        btn.textContent = n > 0 ? `删除选中 (${n})` : '批量删除';
    }
}

async function batchDeleteHistory() {
    const n = state.selectedHistory.size;
    if (n === 0 || !confirm(`确定删除选中的 ${n} 个对话？`)) return;
    try {
        await fetchApi(`${API_BASE}/api/sessions/batch-delete`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ ids: Array.from(state.selectedHistory) })
        });
        state.selectedHistory.clear();
        await loadSessions();
        renderHistory(state.sessions);
    } catch (e) { console.error('批量删除失败:', e); }
}

// ============================================================================
// 输入处理
// ============================================================================

function setupInputHandlers() {
    const input = document.getElementById('chat-input');
    input.addEventListener('input', () => {
        updateSendButton();
        autoResize(input);
    });
}

function handleInputKeydown(e) {
    if (e.key === 'Enter' && !e.shiftKey) {
        e.preventDefault();
        sendMessage();
    }
}

function updateSendButton() {
    const input = document.getElementById('chat-input');
    const btn = document.getElementById('btn-send');
    btn.disabled = !input.value.trim() || !state.activeSessionId;
}

function autoResize(textarea) {
    textarea.style.height = 'auto';
    textarea.style.height = Math.min(textarea.scrollHeight, 150) + 'px';
}

// ============================================================================
// 快捷键
// ============================================================================

document.addEventListener('keydown', (e) => {
    if (e.ctrlKey) {
        switch (e.key) {
            case 'n':
                e.preventDefault();
                createNewSession();
                break;
            case 'w':
                e.preventDefault();
                if (state.activeSessionId) deleteSession(state.activeSessionId);
                break;
            case 's':
                e.preventDefault();
                if (state.editingConfig) saveConfig();
                break;
            case ',':
                e.preventDefault();
                switchView('settings');
                break;
        }
    }
    if (e.key === 'Escape') {
        const menu = document.querySelector('.context-menu');
        if (menu) { menu.remove(); } else { switchView('chat'); }
    }
    if (e.ctrlKey && e.key === 'a' && state.activeView === 'history') {
        e.preventDefault();
        toggleSelectAllHistory();
    }
    if (e.key === 'Delete' && state.activeView === 'history' && state.selectedHistory.size > 0) {
        batchDeleteHistory();
    }
});

// ============================================================================
// 右键菜单
// ============================================================================

function showSessionMenu(event, sessionId) {
    event.preventDefault();
    
    // 移除现有菜单
    const existingMenu = document.querySelector('.context-menu');
    if (existingMenu) existingMenu.remove();
    
    const menu = document.createElement('div');
    menu.className = 'context-menu';
    menu.style.cssText = `
        position: fixed;
        left: ${event.clientX}px;
        top: ${event.clientY}px;
        background: var(--bg-tertiary);
        border-radius: 8px;
        padding: 4px;
        box-shadow: 0 4px 12px rgba(0,0,0,0.3);
        z-index: 1000;
    `;
    
const session = state.sessions.find(s => s.id === sessionId);
        const items = [
            { label: '重命名', action: () => renameSession(sessionId) },
            { label: session?.pinned ? '取消置顶' : '置顶', action: () => pinSession(sessionId) },
            { label: '导出', action: () => exportSession(sessionId) },
        { label: '删除', action: () => deleteSession(sessionId), danger: true }
    ];
    
    items.forEach(item => {
        const btn = document.createElement('button');
        btn.textContent = item.label;
        btn.style.cssText = `
            display: block;
            width: 100%;
            padding: 8px 16px;
            background: none;
            border: none;
            color: ${item.danger ? 'var(--error)' : 'var(--text-primary)'};
            font-size: 13px;
            text-align: left;
            cursor: pointer;
            border-radius: 4px;
        `;
        btn.onmouseover = () => btn.style.background = 'var(--bg-primary)';
        btn.onmouseout = () => btn.style.background = 'none';
        btn.onclick = () => {
            menu.remove();
            item.action();
        };
        menu.appendChild(btn);
    });
    
    document.body.appendChild(menu);
    
    // 点击其他地方关闭菜单
    document.addEventListener('click', () => menu.remove(), { once: true });
}

async function renameSession(sessionId) {
    const session = state.sessions.find(s => s.id === sessionId);
    if (!session) return;
    const newTitle = prompt('输入新名称:', session.title);
    if (!newTitle || newTitle === session.title) return;
    
    try {
        await fetchApi(`${API_BASE}/api/sessions/${sessionId}/rename`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ title: newTitle })
        });
        await loadSessions();
    } catch (e) { console.error('重命名失败:', e); }
}

async function pinSession(sessionId) {
    try {
        await fetchApi(`${API_BASE}/api/sessions/${sessionId}/pin`, { method: 'PUT' });
        await loadSessions();
    } catch (e) { console.error('置顶失败:', e); }
}

async function exportSession(sessionId) {
    try {
        const res = await fetchApi(`${API_BASE}/api/sessions/${sessionId}/messages`);
        if (!res.ok) return;
        const messages = await res.json();
        const session = state.sessions.find(s => s.id === sessionId);
        
        let md = `# ${session?.title || '对话'}\n\n`;
        md += `导出时间: ${new Date().toLocaleString('zh-CN')}\n\n---\n\n`;
        messages.forEach(m => {
            const role = m.role === 'user' ? '👤 用户' : m.role === 'assistant' ? '🤖 助手' : '⚙ 系统';
            md += `### ${role}\n\n${m.content}\n\n`;
        });
        
        const blob = new Blob([md], { type: 'text/markdown' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `${session?.title || '对话'}.md`;
        a.click();
        URL.revokeObjectURL(url);
    } catch (e) { console.error('导出失败:', e); }
}

// ============================================================================
// Toast 通知
// ============================================================================

function showToast(message, type = 'info') {
    let container = document.getElementById('toast-container');
    if (!container) {
        container = document.createElement('div');
        container.id = 'toast-container';
        container.style.cssText = 'position:fixed;top:16px;right:16px;z-index:9999;display:flex;flex-direction:column;gap:8px;pointer-events:none;';
        document.body.appendChild(container);
    }
    
    const toast = document.createElement('div');
    const colors = {
        success: { bg: '#2d6a4f', border: '#52b788' },
        error:   { bg: '#6a2d2d', border: '#e76f51' },
        warning: { bg: '#6a5a2d', border: '#e9c46a' },
        info:    { bg: '#2d4a6a', border: '#48cae4' },
    };
    const c = colors[type] || colors.info;
    toast.style.cssText = `background:${c.bg};border:1px solid ${c.border};color:#fff;padding:12px 20px;border-radius:8px;font-size:13px;pointer-events:auto;opacity:0;transition:opacity .3s;max-width:420px;word-break:break-word;box-shadow:0 4px 12px rgba(0,0,0,.4);`;
    toast.textContent = message;
    container.appendChild(toast);
    requestAnimationFrame(() => toast.style.opacity = '1');
    
    setTimeout(() => {
        toast.style.opacity = '0';
        setTimeout(() => toast.remove(), 300);
    }, 3500);
}

// ============================================================================
// 工具函数
// ============================================================================

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

function escapeAttr(text) {
    return (text || '').replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/'/g, '&#39;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

function formatTime(isoString) {
    if (!isoString) return '';
    const date = new Date(isoString);
    const now = new Date();
    const diff = now - date;
    
    if (diff < 60000) return '刚刚';
    if (diff < 3600000) return `${Math.floor(diff / 60000)} 分钟前`;
    if (diff < 86400000) return `${Math.floor(diff / 3600000)} 小时前`;
    
    return date.toLocaleDateString('zh-CN', { 
        month: '2-digit', 
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit'
    });
}

function simpleMarkdown(text) {
    if (!text) return '';
    let html = text
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;');
    
    // 代码块
    html = html.replace(/```(\w*)\n([\s\S]*?)```/g, '<pre><code>$2</code></pre>');
    // 标题
    html = html.replace(/^### (.+)$/gm, '<h3>$1</h3>');
    html = html.replace(/^## (.+)$/gm, '<h2>$1</h2>');
    html = html.replace(/^# (.+)$/gm, '<h1>$1</h1>');
    // 粗体/斜体
    html = html.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>');
    html = html.replace(/\*(.+?)\*/g, '<em>$1</em>');
    // 行内代码
    html = html.replace(/`(.+?)`/g, '<code>$1</code>');
    // 引用
    html = html.replace(/^&gt; (.+)$/gm, '<blockquote>$1</blockquote>');
    // 无序列表
    html = html.replace(/^[*-] (.+)$/gm, '<li>$1</li>');
    html = html.replace(/(<li>.*<\/li>\n?)+/g, '<ul>$&</ul>');
    // 有序列表
    html = html.replace(/^\d+\. (.+)$/gm, '<li>$1</li>');
    // 分隔线
    html = html.replace(/^---$/gm, '<hr>');
    // 链接
    html = html.replace(/\[(.+?)\]\((.+?)\)/g, '<a href="$2" target="_blank">$1</a>');
    // 换行
    html = html.replace(/\n/g, '<br>');
    
    return html;
}

// ============================================================================
// Host 连接状态监控
// ============================================================================

function startHostStatusMonitor() {
    checkHostStatus();
    setInterval(checkHostStatus, 5000);
}

async function checkHostStatus() {
    const statusEl = document.getElementById('host-status');
    const dotEl = document.getElementById('host-dot');
    const textEl = document.getElementById('host-status-text');

    try {
        const res = await fetch('http://localhost:7300/health', {
            signal: AbortSignal.timeout(3000)
        });
        if (res.ok) {
            state.coreStatus = 'running';
            statusEl.className = 'status-indicator connected';
            textEl.textContent = '已连接';
        } else {
            state.coreStatus = 'stopped';
            statusEl.className = 'status-indicator disconnected';
            textEl.textContent = '服务异常';
        }
    } catch {
        state.coreStatus = 'stopped';
        statusEl.className = 'status-indicator disconnected';
        textEl.textContent = '未连接';
    }
}

