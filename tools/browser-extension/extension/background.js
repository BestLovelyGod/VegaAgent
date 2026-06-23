// ============================================================================
// Ignorant Vega — Browser Extension Background Service Worker
// 通过 Native Messaging 与 Agent 通信，提供深度浏览器自动化能力
// ============================================================================

const NATIVE_HOST_NAME = "com.ignorantvega.browser";
const AGENT_API_BASE = "http://localhost:7300";

let nativePort = null;
let commandQueue = [];
let commandCallbacks = new Map();
let commandIdCounter = 0;

// ── Native Messaging 连接管理 ──────────────────────────────────────────────

function connectNativeHost() {
  if (nativePort) return;
  
  try {
    nativePort = chrome.runtime.connectNative(NATIVE_HOST_NAME);
    
    nativePort.onMessage.addListener((msg) => {
      console.log("[Vega] 收到 Native 消息:", msg);
      
      if (msg.commandId && commandCallbacks.has(msg.commandId)) {
        const callback = commandCallbacks.get(msg.commandId);
        commandCallbacks.delete(msg.commandId);
        callback(msg);
      }
      
      // 处理来自 Agent 的指令
      if (msg.type === "agent_command") {
        handleAgentCommand(msg);
      }
    });
    
    nativePort.onDisconnect.addListener(() => {
      console.log("[Vega] Native 连接断开:", chrome.runtime.lastError?.message);
      nativePort = null;
      // 3 秒后重连
      setTimeout(connectNativeHost, 3000);
    });
    
    console.log("[Vega] Native Messaging 已连接");
  } catch (e) {
    console.error("[Vega] Native 连接失败:", e.message);
    setTimeout(connectNativeHost, 5000);
  }
}

// 启动时连接
connectNativeHost();

// ── 命令处理器 ─────────────────────────────────────────────────────────────

async function handleAgentCommand(msg) {
  const { commandId, action, params } = msg;
  
  try {
    let result;
    
    switch (action) {
      // ── 页面交互 ──
      case "navigate":
        result = await cmdNavigate(params.url, params.tabId);
        break;
      case "click":
        result = await cmdClick(params.selector, params.tabId);
        break;
      case "fill":
        result = await cmdFill(params.selector, params.value, params.tabId);
        break;
      case "extract":
        result = await cmdExtract(params.selector, params.tabId);
        break;
      case "evaluate":
        result = await cmdEvaluate(params.js, params.tabId);
        break;
      case "screenshot":
        result = await cmdScreenshot(params.fullPage, params.tabId);
        break;
      
      // ── Cookie 管理 ──
      case "getCookies":
        result = await cmdGetCookies(params.url, params.name);
        break;
      case "setCookie":
        result = await cmdSetCookie(params);
        break;
      case "removeCookie":
        result = await cmdRemoveCookie(params.url, params.name);
        break;
      
      // ── 标签页管理 ──
      case "getTabs":
        result = await cmdGetTabs();
        break;
      case "createTab":
        result = await cmdCreateTab(params.url, params.active);
        break;
      case "closeTab":
        result = await cmdCloseTab(params.tabId);
        break;
      case "switchTab":
        result = await cmdSwitchTab(params.tabId);
        break;
      
      // ── 网络拦截 ──
      case "getNetworkRequests":
        result = await cmdGetNetworkRequests(params.tabId);
        break;
      
      // ── 存储 ──
      case "getLocalStorage":
        result = await cmdGetLocalStorage(params.keys, params.tabId);
        break;
      case "setLocalStorage":
        result = await cmdSetLocalStorage(params.data, params.tabId);
        break;
      
      // ── 高级 DOM ──
      case "waitForSelector":
        result = await cmdWaitForSelector(params.selector, params.timeout, params.tabId);
        break;
      case "getTextContent":
        result = await cmdGetTextContent(params.selector, params.tabId);
        break;
      case "getAttributes":
        result = await cmdGetAttributes(params.selector, params.attributes, params.tabId);
        break;
      case "scrollTo":
        result = await cmdScrollTo(params.selector, params.tabId);
        break;
      case "hover":
        result = await cmdHover(params.selector, params.tabId);
        break;
      case "pressKey":
        result = await cmdPressKey(params.key, params.modifiers, params.tabId);
        break;
      case "uploadFile":
        result = await cmdUploadFile(params.selector, params.filePaths, params.tabId);
        break;
      
      default:
        result = { error: `未知命令: ${action}` };
    }
    
    sendNativeResponse(commandId, { success: true, data: result });
  } catch (e) {
    sendNativeResponse(commandId, { success: false, error: e.message });
  }
}

function sendNativeResponse(commandId, response) {
  if (nativePort) {
    nativePort.postMessage({ commandId, ...response });
  }
}

// ── 页面交互命令 ────────────────────────────────────────────────────────────

async function cmdNavigate(url, tabId) {
  const tab = tabId 
    ? await chrome.tabs.update(tabId, { url })
    : await chrome.tabs.update({ url });
  
  // 等待页面加载完成
  return new Promise((resolve) => {
    const listener = (id, changeInfo) => {
      if (id === tab.id && changeInfo.status === "complete") {
        chrome.tabs.onUpdated.removeListener(listener);
        resolve({ tabId: tab.id, url, loaded: true });
      }
    };
    chrome.tabs.onUpdated.addListener(listener);
    // 30 秒超时
    setTimeout(() => {
      chrome.tabs.onUpdated.removeListener(listener);
      resolve({ tabId: tab.id, url, loaded: false, timeout: true });
    }, 30000);
  });
}

async function cmdClick(selector, tabId) {
  const tab = await getActiveTab(tabId);
  const results = await chrome.scripting.executeScript({
    target: { tabId: tab.id },
    func: (sel) => {
      const el = document.querySelector(sel);
      if (!el) return { error: `元素未找到: ${sel}` };
      
      // 模拟真实鼠标事件
      const rect = el.getBoundingClientRect();
      const x = rect.x + rect.width / 2;
      const y = rect.y + rect.height / 2;
      
      el.dispatchEvent(new MouseEvent("mouseover", { bubbles: true, clientX: x, clientY: y }));
      el.dispatchEvent(new MouseEvent("mousedown", { bubbles: true, clientX: x, clientY: y }));
      el.dispatchEvent(new MouseEvent("mouseup", { bubbles: true, clientX: x, clientY: y }));
      el.dispatchEvent(new MouseEvent("click", { bubbles: true, clientX: x, clientY: y }));
      
      return { clicked: sel, tagName: el.tagName, text: el.textContent?.slice(0, 100) };
    },
    args: [selector]
  });
  return results[0]?.result;
}

async function cmdFill(selector, value, tabId) {
  const tab = await getActiveTab(tabId);
  const results = await chrome.scripting.executeScript({
    target: { tabId: tab.id },
    func: (sel, val) => {
      const el = document.querySelector(sel);
      if (!el) return { error: `元素未找到: ${sel}` };
      
      // 聚焦
      el.focus();
      el.dispatchEvent(new FocusEvent("focus", { bubbles: true }));
      
      // 清空并设置值
      el.value = "";
      el.dispatchEvent(new Event("input", { bubbles: true }));
      
      // 逐字符输入（模拟真实键盘）
      for (const char of val) {
        el.value += char;
        el.dispatchEvent(new KeyboardEvent("keydown", { key: char, bubbles: true }));
        el.dispatchEvent(new KeyboardEvent("keypress", { key: char, bubbles: true }));
        el.dispatchEvent(new Event("input", { bubbles: true, data: char }));
        el.dispatchEvent(new KeyboardEvent("keyup", { key: char, bubbles: true }));
      }
      
      el.dispatchEvent(new Event("change", { bubbles: true }));
      el.dispatchEvent(new FocusEvent("blur", { bubbles: true }));
      
      return { filled: sel, value: el.value };
    },
    args: [selector, value]
  });
  return results[0]?.result;
}

async function cmdExtract(selector, tabId) {
  const tab = await getActiveTab(tabId);
  const results = await chrome.scripting.executeScript({
    target: { tabId: tab.id },
    func: (sel) => {
      const elements = document.querySelectorAll(sel);
      return Array.from(elements).map(el => ({
        tag: el.tagName,
        text: el.textContent?.trim(),
        html: el.innerHTML?.slice(0, 500),
        href: el.href || null,
        src: el.src || null,
        value: el.value || null
      }));
    },
    args: [selector]
  });
  return results[0]?.result;
}

async function cmdEvaluate(js, tabId) {
  const tab = await getActiveTab(tabId);
  const results = await chrome.scripting.executeScript({
    target: { tabId: tab.id },
    func: (code) => {
      try {
        const result = eval(code);
        return { result: typeof result === "object" ? JSON.stringify(result) : String(result) };
      } catch (e) {
        return { error: e.message };
      }
    },
    args: [js]
  });
  return results[0]?.result;
}

async function cmdScreenshot(fullPage, tabId) {
  const tab = await getActiveTab(tabId);
  
  if (fullPage) {
    // 全页截图需要 CDP
    try {
      await chrome.debugger.attach({ tabId: tab.id }, "1.3");
      const { data } = await chrome.debugger.sendCommand(
        { tabId: tab.id }, "Page.captureScreenshot", { format: "png" }
      );
      await chrome.debugger.detach({ tabId: tab.id });
      return { dataUrl: `data:image/png;base64,${data}`, width: 0, height: 0 };
    } catch (e) {
      return { error: `CDP 截图失败: ${e.message}` };
    }
  }
  
  // 可视区域截图
  const dataUrl = await chrome.tabs.captureVisibleTab(tab.windowId, { format: "png" });
  return { dataUrl };
}

// ── Cookie 管理 ─────────────────────────────────────────────────────────────

async function cmdGetCookies(url, name) {
  const details = url ? { url } : {};
  if (name) details.name = name;
  const cookies = await chrome.cookies.getAll(details);
  return cookies;
}

async function cmdSetCookie(params) {
  const details = {
    url: params.url,
    name: params.name,
    value: params.value,
    path: params.path || "/",
    secure: params.secure || false,
    httpOnly: params.httpOnly || false,
    sameSite: params.sameSite || "lax"
  };
  if (params.domain) details.domain = params.domain;
  if (params.expirationDate) details.expirationDate = params.expirationDate;
  
  const cookie = await chrome.cookies.set(details);
  return cookie;
}

async function cmdRemoveCookie(url, name) {
  await chrome.cookies.remove({ url, name });
  return { removed: name };
}

// ── 标签页管理 ──────────────────────────────────────────────────────────────

async function cmdGetTabs() {
  const tabs = await chrome.tabs.query({});
  return tabs.map(t => ({
    id: t.id,
    url: t.url,
    title: t.title,
    active: t.active,
    windowId: t.windowId,
    status: t.status
  }));
}

async function cmdCreateTab(url, active = true) {
  const tab = await chrome.tabs.create({ url, active });
  return { id: tab.id, url: tab.url, title: tab.title };
}

async function cmdCloseTab(tabId) {
  await chrome.tabs.remove(tabId);
  return { closed: tabId };
}

async function cmdSwitchTab(tabId) {
  await chrome.tabs.update(tabId, { active: true });
  const tab = await chrome.tabs.get(tabId);
  return { id: tab.id, url: tab.url, title: tab.title };
}

// ── 网络请求监控 ────────────────────────────────────────────────────────────

const networkRequests = new Map(); // tabId -> requests[]

chrome.webRequest.onCompleted.addListener(
  (details) => {
    if (details.tabId < 0) return;
    if (!networkRequests.has(details.tabId)) {
      networkRequests.set(details.tabId, []);
    }
    const requests = networkRequests.get(details.tabId);
    requests.push({
      url: details.url,
      method: details.method,
      status: details.statusCode,
      type: details.type,
      timestamp: details.timeStamp
    });
    // 最多保留 200 条
    if (requests.length > 200) requests.splice(0, requests.length - 200);
  },
  { urls: ["<all_urls>"] }
);

async function cmdGetNetworkRequests(tabId) {
  const tab = await getActiveTab(tabId);
  return networkRequests.get(tab.id) || [];
}

// ── localStorage 管理 ──────────────────────────────────────────────────────

async function cmdGetLocalStorage(keys, tabId) {
  const tab = await getActiveTab(tabId);
  const results = await chrome.scripting.executeScript({
    target: { tabId: tab.id },
    func: (ks) => {
      if (!ks || ks.length === 0) {
        // 返回全部
        const all = {};
        for (let i = 0; i < localStorage.length; i++) {
          const key = localStorage.key(i);
          all[key] = localStorage.getItem(key);
        }
        return all;
      }
      const result = {};
      for (const k of ks) {
        result[k] = localStorage.getItem(k);
      }
      return result;
    },
    args: [keys]
  });
  return results[0]?.result;
}

async function cmdSetLocalStorage(data, tabId) {
  const tab = await getActiveTab(tabId);
  const results = await chrome.scripting.executeScript({
    target: { tabId: tab.id },
    func: (d) => {
      for (const [key, value] of Object.entries(d)) {
        localStorage.setItem(key, value);
      }
      return { set: Object.keys(d) };
    },
    args: [data]
  });
  return results[0]?.result;
}

// ── 高级 DOM 操作 ──────────────────────────────────────────────────────────

async function cmdWaitForSelector(selector, timeout = 10000, tabId) {
  const tab = await getActiveTab(tabId);
  const results = await chrome.scripting.executeScript({
    target: { tabId: tab.id },
    func: (sel, ms) => {
      return new Promise((resolve) => {
        const existing = document.querySelector(sel);
        if (existing) {
          resolve({ found: true, immediate: true, tagName: existing.tagName });
          return;
        }
        
        const observer = new MutationObserver(() => {
          const el = document.querySelector(sel);
          if (el) {
            observer.disconnect();
            clearTimeout(timer);
            resolve({ found: true, tagName: el.tagName });
          }
        });
        
        observer.observe(document.body, { childList: true, subtree: true });
        
        const timer = setTimeout(() => {
          observer.disconnect();
          resolve({ found: false, timeout: true });
        }, ms);
      });
    },
    args: [selector, timeout]
  });
  return results[0]?.result;
}

async function cmdGetTextContent(selector, tabId) {
  const tab = await getActiveTab(tabId);
  const results = await chrome.scripting.executeScript({
    target: { tabId: tab.id },
    func: (sel) => {
      const el = document.querySelector(sel);
      if (!el) return { error: `元素未找到: ${sel}` };
      return { text: el.textContent?.trim(), innerText: el.innerText?.trim() };
    },
    args: [selector]
  });
  return results[0]?.result;
}

async function cmdGetAttributes(selector, attributes, tabId) {
  const tab = await getActiveTab(tabId);
  const results = await chrome.scripting.executeScript({
    target: { tabId: tab.id },
    func: (sel, attrs) => {
      const el = document.querySelector(sel);
      if (!el) return { error: `元素未找到: ${sel}` };
      const result = {};
      const attrList = attrs && attrs.length > 0 ? attrs : el.getAttributeNames();
      for (const a of attrList) {
        result[a] = el.getAttribute(a);
      }
      return result;
    },
    args: [selector, attributes]
  });
  return results[0]?.result;
}

async function cmdScrollTo(selector, tabId) {
  const tab = await getActiveTab(tabId);
  const results = await chrome.scripting.executeScript({
    target: { tabId: tab.id },
    func: (sel) => {
      const el = document.querySelector(sel);
      if (!el) return { error: `元素未找到: ${sel}` };
      el.scrollIntoView({ behavior: "smooth", block: "center" });
      return { scrolledTo: sel };
    },
    args: [selector]
  });
  return results[0]?.result;
}

async function cmdHover(selector, tabId) {
  const tab = await getActiveTab(tabId);
  const results = await chrome.scripting.executeScript({
    target: { tabId: tab.id },
    func: (sel) => {
      const el = document.querySelector(sel);
      if (!el) return { error: `元素未找到: ${sel}` };
      const rect = el.getBoundingClientRect();
      const x = rect.x + rect.width / 2;
      const y = rect.y + rect.height / 2;
      el.dispatchEvent(new MouseEvent("mouseenter", { bubbles: true, clientX: x, clientY: y }));
      el.dispatchEvent(new MouseEvent("mouseover", { bubbles: true, clientX: x, clientY: y }));
      el.dispatchEvent(new MouseEvent("mousemove", { bubbles: true, clientX: x, clientY: y }));
      return { hovered: sel, tagName: el.tagName };
    },
    args: [selector]
  });
  return results[0]?.result;
}

async function cmdPressKey(key, modifiers = [], tabId) {
  const tab = await getActiveTab(tabId);
  const results = await chrome.scripting.executeScript({
    target: { tabId: tab.id },
    func: (k, mods) => {
      const target = document.activeElement || document.body;
      const opts = {
        key: k,
        code: k.length === 1 ? `Key${k.toUpperCase()}` : k,
        bubbles: true,
        ctrlKey: mods.includes("ctrl"),
        shiftKey: mods.includes("shift"),
        altKey: mods.includes("alt"),
        metaKey: mods.includes("meta")
      };
      target.dispatchEvent(new KeyboardEvent("keydown", opts));
      target.dispatchEvent(new KeyboardEvent("keypress", opts));
      target.dispatchEvent(new KeyboardEvent("keyup", opts));
      return { pressed: k, modifiers: mods };
    },
    args: [key, modifiers]
  });
  return results[0]?.result;
}

async function cmdUploadFile(selector, filePaths, tabId) {
  // 文件上传需要通过 CDP 的 DOM.setFileInputFiles
  const tab = await getActiveTab(tabId);
  try {
    await chrome.debugger.attach({ tabId: tab.id }, "1.3");
    
    // 获取元素的 backendNodeId
    const { root } = await chrome.debugger.sendCommand(
      { tabId: tab.id }, "DOM.getDocument", { depth: -1 }
    );
    const { nodeId } = await chrome.debugger.sendCommand(
      { tabId: tab.id }, "DOM.querySelector", {
        nodeId: root.nodeId,
        selector: selector
      }
    );
    const { backendNodeId } = await chrome.debugger.sendCommand(
      { tabId: tab.id }, "DOM.resolveNode", { nodeId }
    );
    
    await chrome.debugger.sendCommand(
      { tabId: tab.id }, "DOM.setFileInputFiles", {
        files: filePaths,
        backendNodeId: backendNodeId
      }
    );
    
    await chrome.debugger.detach({ tabId: tab.id });
    return { uploaded: filePaths, selector };
  } catch (e) {
    try { await chrome.debugger.detach({ tabId: tab.id }); } catch {}
    return { error: `文件上传失败: ${e.message}` };
  }
}

// ── 工具函数 ────────────────────────────────────────────────────────────────

async function getActiveTab(tabId) {
  if (tabId) {
    return await chrome.tabs.get(tabId);
  }
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  if (!tab) throw new Error("没有活动标签页");
  return tab;
}

// ── 长轮询模式（无 Native Messaging 时的备用方案）──────────────────────────

let polling = false;

async function pollAgentCommands() {
  if (polling) return;
  polling = true;
  
  while (polling) {
    try {
      const resp = await fetch(`${AGENT_API_BASE}/api/browser/pending`);
      if (resp.ok) {
        const commands = await resp.json();
        for (const cmd of commands) {
          await handleAgentCommand(cmd);
        }
      }
    } catch (e) {
      // Agent 未启动，静默重试
    }
    await new Promise(r => setTimeout(r, 1000));
  }
}

// 如果 Native Messaging 连接失败，启动轮询模式
setTimeout(() => {
  if (!nativePort) {
    console.log("[Vega] Native Messaging 不可用，启用轮询模式");
    pollAgentCommands();
  }
}, 10000);

// ── 消息处理（来自 popup 或 content script）─────────────────────────────────

chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (msg.type === "execute_command") {
    handleAgentCommand(msg).then(result => sendResponse(result));
    return true; // 异步响应
  }
  
  if (msg.type === "get_status") {
    sendResponse({
      connected: !!nativePort,
      agentUrl: AGENT_API_BASE,
      mode: nativePort ? "native" : "polling"
    });
    return true;
  }
});
