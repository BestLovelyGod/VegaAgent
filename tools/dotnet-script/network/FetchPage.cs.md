# FetchPage.cs

> 路径: `tools/dotnet-script/network/FetchPage.cs`
> 类型: DotnetScript 单文件工具（ToolScanner 自动发现注册）

## 职责

网页抓取工具 — 获取任意 URL 的 HTML 内容，提取可读文本或 Markdown 格式输出，供 LLM 直接消费。

## 接口

```
FetchPage <url> [maxChars] [format]
```

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `url` | string | (必需) | 目标网页 URL，必须是 http/https |
| `maxChars` | int | 8000 | 最大输出字符数，范围 100~100000 |
| `format` | string | `text` | 输出格式：`text`（纯文本）或 `markdown`（Markdown） |

## 输出格式

JSON 结构：

```json
{
  "timestamp": "2026-06-24 02:18:21",
  "url": "https://example.com",
  "title": "Example Domain",
  "contentType": "text/html",
  "contentLength": 559,
  "format": "text",
  "truncated": false,
  "error": null,
  "content": "Example Domain\n\nThis domain is for use in documentation..."
}
```

| 字段 | 说明 |
|------|------|
| `timestamp` | 执行时间 |
| `url` | 请求的 URL |
| `title` | 页面 `<title>` 标签内容 |
| `contentType` | HTTP 响应的 Content-Type |
| `contentLength` | 原始 HTML 长度 |
| `format` | 实际输出格式 |
| `truncated` | 是否因 maxChars 截断 |
| `error` | 错误信息（成功时为 null） |
| `content` | 提取的正文内容 |

## 使用示例

```bash
# 默认文本模式，8000 字符上限
FetchPage "https://example.com"

# Markdown 格式，4000 字符上限
FetchPage "https://learn.microsoft.com/dotnet/csharp" 4000 markdown

# 小幅截取
FetchPage "https://www.baidu.com" 1500 text
```

## 内部实现

### HTML 清理流程

1. **移除无用标签** — `<script>`, `<style>`, `<noscript>`, `<svg>`, `<link>`, `<meta>`, `<template>`
2. **移除 HTML 注释** — `<!-- ... -->`
3. **text 模式**: 块级元素转换行 → 移除所有标签 → 解码实体 → 合并空白
4. **markdown 模式**: 标题转 `#`、强调转 `**`/`*`、链接转 `[text](url)`、代码块转 `` ``` ``、列表转 `- `

### 网络行为

- 随机 User-Agent（3 个预设）
- 20 秒超时
- 最多 2 次重试（指数退避）
- 自动解压 GZip/Deflate
- 自动跟随重定向（最多 5 次）

## 注意事项

- 纯静态 HTML 抓取，不支持 JavaScript 渲染的 SPA 页面
- 部分网站（如 GitHub）会拒绝非浏览器请求，返回连接错误
- 与 `SearchWeb.cs` 的区别：SearchWeb 搜索引擎结果解析，FetchPage 抓取指定 URL 的完整内容
- 工具通过 `dotnet run` 临时项目执行，首次运行有编译延迟
- JSON 输出使用 `JsonSerializerContext`（AOT 兼容）
