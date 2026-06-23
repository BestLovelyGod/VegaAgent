# Ignorant Vega Browser Bridge

Edge 浏览器扩展，为 Agent 提供深度浏览器自动化能力。

## 架构

```
┌─────────────────────────────────────┐
│  Edge 浏览器                         │
│  ┌───────────────────────────────┐  │
│  │  Browser Extension (MV3)      │  │
│  │  • Content Scripts (DOM 操作) │  │
│  │  • Background Service Worker  │  │
│  │  • Cookies / Storage / Tabs   │  │
│  └───────────┬───────────────────┘  │
└──────────────┼──────────────────────┘
               │ HTTP API
┌──────────────┼──────────────────────┐
│  Agent.Host (localhost:7300)        │
│  /api/browser/* 端点                │
└─────────────────────────────────────┘
```

## 能力对比

| 能力 | CDP 模式 (EdgeBrowser) | 扩展模式 (Browser) |
|------|----------------------|-------------------|
| 页面导航 | ✅ | ✅ |
| 点击元素 | ⚠️ JS 注入 | ✅ 真实鼠标事件 |
| 填写表单 | ⚠️ 直接设值 | ✅ 逐字符输入 |
| 截图 | ✅ | ✅ |
| 执行 JS | ✅ | ✅ |
| Cookie 管理 | ❌ | ✅ |
| localStorage | ❌ | ✅ |
| 多标签页管理 | ❌ | ✅ |
| 网络请求监控 | ❌ | ✅ |
| 元素等待 | ❌ | ✅ |
| 鼠标悬停 | ❌ | ✅ |
| 键盘按键 | ❌ | ✅ |
| 文件上传 | ❌ | ✅ |
| DOM 变化观察 | ❌ | ✅ |
| 跨域请求 | ❌ | ✅ |
| 登录态保持 | ❌ | ✅ |

## 安装

```powershell
# 1. 运行安装脚本
cd tools\browser-extension
.\install.ps1

# 2. 在 Edge 中加载扩展
#    edge://extensions/ → 开发者模式 → 加载解压缩的扩展
#    选择: tools\browser-extension\extension
```

## 使用

Agent 会自动使用 `browser` 工具（优先级高于 `EdgeBrowser`）：

```
用户: 打开百度搜索 ".NET 教程"
Agent: browser(action=navigate, url="https://www.baidu.com")
       browser(action=fill, selector="#kw", value=".NET 教程")
       browser(action=click, selector="#su")
       browser(action=extract, selector=".result")
```

## 命令列表

| 命令 | 参数 | 说明 |
|------|------|------|
| `navigate` | url, tabId? | 导航到 URL |
| `click` | selector, tabId? | 点击元素 |
| `fill` | selector, value, tabId? | 填写表单 |
| `extract` | selector, tabId? | 提取元素内容 |
| `evaluate` | js, tabId? | 执行 JavaScript |
| `screenshot` | fullPage?, tabId? | 截图 |
| `getCookies` | url?, name? | 获取 Cookie |
| `setCookie` | url, name, value, ... | 设置 Cookie |
| `removeCookie` | url, name | 删除 Cookie |
| `getTabs` | — | 列出所有标签页 |
| `createTab` | url, active? | 创建新标签页 |
| `closeTab` | tabId | 关闭标签页 |
| `switchTab` | tabId | 切换标签页 |
| `waitForSelector` | selector, timeout? | 等待元素出现 |
| `hover` | selector | 鼠标悬停 |
| `pressKey` | key, modifiers? | 按键 |
| `scrollTo` | selector | 滚动到元素 |
| `getLocalStorage` | keys?, tabId? | 获取 localStorage |
| `setLocalStorage` | data, tabId? | 设置 localStorage |
| `uploadFile` | selector, filePaths | 上传文件 |
