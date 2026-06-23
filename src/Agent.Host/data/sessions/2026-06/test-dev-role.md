# ?????
> SessionId: test-dev-role | 2026-06-02 20:44:35 | 消息数: 3

## developer
你是 Ignorant Vega，Windows PC 智能管家。你必须通过 function calling 调用工具来完成任务。

=== 核心判断原则 ===

你的训练数据有截止日期。当用户请求的信息属于以下类别时，必须调用工具获取，不要凭记忆回答：

1. **实时信息** — 天气、新闻、股价、赛事比分、当前时间等
2. **外部知识** — 人物、地点、产品、概念、教程等你不确定或可能过时的信息
3. **用户设备状态** — 系统信息、进程、文件、网络等本机数据
4. **需要执行的操作** — 运行命令、管理文件、浏览器操作等

判断逻辑:
- 信息是实时/外部/不确定的 → SearchWeb 搜索
- 信息是本机相关的 → system-ops / file-ops / powershell
- 天气查询 → net-ops (action="Get-Weather")
- **打开/启动程序** → run-process (FileName=应用名，如 "记事本"、"微信")
- 你确定且不会过时的知识 → 可以直接回答

=== 调用示例 ===
用户: "打开记事本" → run-process(FileName="notepad")
用户: "打开微信" → run-process(FileName="微信")
用户: "帮我打开计算器" → run-process(FileName="calc")
用户: "查看系统信息" → system-ops(action="info")
用户: "清理临时文件" → file-ops(action="clean")

=== 重要规则 ===
1. 必须通过 function calling 调用工具，不要在文本中写命令
2. 如果工具调用失败，必须告诉用户失败原因，不要返回空回复
3. 中文回复
4. **搜索限制**: SearchWeb 最多调用 2 次。如果 2 次搜索后仍无满意结果，必须基于已有信息回答，告知用户信息可能不完整
5. **执行操作**: 用户要求执行操作（如清空回收站、打开程序、清理文件等）时，直接调用对应工具执行，不要反复确认或搜索教程

# 用户自定义设定

> 此文件的内容会追加到核心提示词之后，用于覆盖或补充默认行为。

## 项目信息

- **项目名称**: Ignorant Vega
- **技术栈**: .NET 10 + ASP.NET Core
- **LLM**: MiMo v2.5

## 额外规则

- 搜索时优先使用 bing 引擎，中文内容可用 baidu
- 不确定的事实先搜索再回答
- 天气查询直接用 `wttr.in/?format=j1`，API 会根据请求 IP 自动定位，无需先获取 IP

## 启动本地程序（重要）

当用户说"打开XX"、"启动XX"、"运行XX"时，**必须**使用 `run-process` 工具：
- `FileName` 直接填应用名即可，如 "记事本"、"微信"、"notepad"、"calc"
- 也支持完整路径如 "C:\\Program Files\\App\\app.exe"
- 不需要先查系统信息，直接调用 `run-process`
- 示例：`{"tool":"run-process","params":{"FileName":"记事本"}}`

## 用户偏好

- **语言**: 中文
- **回复风格**: 简洁直接，不需要过多客套
- **技术栈**: .NET 10, PowerShell, C#
- **编辑器**: VS Code
- **终端**: PowerShell 7


# 用户偏好

> 此文件由 Agent 自动维护，记录从对话中学到的用户偏好和习惯。
> 用户也可以手动编辑此文件来覆盖 Agent 的学习结果。

## 语言与风格

- **语言**: 中文
- **回复风格**: 简洁直接

## 技术偏好

- **技术栈**: .NET 10, PowerShell, C#
- **编辑器**: VS Code
- **终端**: PowerShell 7

## 行为习惯

- （Agent 会自动从对话中提取并记录）

---

# 当前上下文

- 当前时间: 2026-06-02 20:44:28 星期二
- 计算机名: DESKTOP-MIAO
- 用户: miao
- 操作系统: Microsoft Windows NT 10.0.26200.0
- .NET 版本: 10.0.8

可通过 powershell 工具调用的扩展脚本:
- CreateIVegaUser: 本地管理员账户 IVega 管理工具 — Agent 自动提权
- EdgeBrowser: Edge 浏览器自动化工具 — 通过 CDP 协议控制 Edge 浏览器
- HashGenerator: 文件/字符串哈希生成工具 — 支持 MD5/SHA1/SHA256/SHA512
- JsonTransformer: JSON 数据转换工具 — 格式化、过滤、合并、查询
- RestClient: HTTP REST 客户端工具 — 支持 GET/POST/PUT/DELETE 请求


## 👤 用户
?????

## 🤖 助手


<!-- SESSION_STATE
[{"Role":"developer","Content":"\u4F60\u662F Ignorant Vega\uFF0CWindows PC \u667A\u80FD\u7BA1\u5BB6\u3002\u4F60\u5FC5\u987B\u901A\u8FC7 function calling \u8C03\u7528\u5DE5\u5177\u6765\u5B8C\u6210\u4EFB\u52A1\u3002\r\n\r\n=== \u6838\u5FC3\u5224\u65AD\u539F\u5219 ===\r\n\r\n\u4F60\u7684\u8BAD\u7EC3\u6570\u636E\u6709\u622A\u6B62\u65E5\u671F\u3002\u5F53\u7528\u6237\u8BF7\u6C42\u7684\u4FE1\u606F\u5C5E\u4E8E\u4EE5\u4E0B\u7C7B\u522B\u65F6\uFF0C\u5FC5\u987B\u8C03\u7528\u5DE5\u5177\u83B7\u53D6\uFF0C\u4E0D\u8981\u51ED\u8BB0\u5FC6\u56DE\u7B54\uFF1A\r\n\r\n1. **\u5B9E\u65F6\u4FE1\u606F** \u2014 \u5929\u6C14\u3001\u65B0\u95FB\u3001\u80A1\u4EF7\u3001\u8D5B\u4E8B\u6BD4\u5206\u3001\u5F53\u524D\u65F6\u95F4\u7B49\r\n2. **\u5916\u90E8\u77E5\u8BC6** \u2014 \u4EBA\u7269\u3001\u5730\u70B9\u3001\u4EA7\u54C1\u3001\u6982\u5FF5\u3001\u6559\u7A0B\u7B49\u4F60\u4E0D\u786E\u5B9A\u6216\u53EF\u80FD\u8FC7\u65F6\u7684\u4FE1\u606F\r\n3. **\u7528\u6237\u8BBE\u5907\u72B6\u6001** \u2014 \u7CFB\u7EDF\u4FE1\u606F\u3001\u8FDB\u7A0B\u3001\u6587\u4EF6\u3001\u7F51\u7EDC\u7B49\u672C\u673A\u6570\u636E\r\n4. **\u9700\u8981\u6267\u884C\u7684\u64CD\u4F5C** \u2014 \u8FD0\u884C\u547D\u4EE4\u3001\u7BA1\u7406\u6587\u4EF6\u3001\u6D4F\u89C8\u5668\u64CD\u4F5C\u7B49\r\n\r\n\u5224\u65AD\u903B\u8F91:\r\n- \u4FE1\u606F\u662F\u5B9E\u65F6/\u5916\u90E8/\u4E0D\u786E\u5B9A\u7684 \u2192 SearchWeb \u641C\u7D22\r\n- \u4FE1\u606F\u662F\u672C\u673A\u76F8\u5173\u7684 \u2192 system-ops / file-ops / powershell\r\n- \u5929\u6C14\u67E5\u8BE2 \u2192 net-ops (action=\u0022Get-Weather\u0022)\r\n- **\u6253\u5F00/\u542F\u52A8\u7A0B\u5E8F** \u2192 run-process (FileName=\u5E94\u7528\u540D\uFF0C\u5982 \u0022\u8BB0\u4E8B\u672C\u0022\u3001\u0022\u5FAE\u4FE1\u0022)\r\n- \u4F60\u786E\u5B9A\u4E14\u4E0D\u4F1A\u8FC7\u65F6\u7684\u77E5\u8BC6 \u2192 \u53EF\u4EE5\u76F4\u63A5\u56DE\u7B54\r\n\r\n=== \u8C03\u7528\u793A\u4F8B ===\r\n\u7528\u6237: \u0022\u6253\u5F00\u8BB0\u4E8B\u672C\u0022 \u2192 run-process(FileName=\u0022notepad\u0022)\r\n\u7528\u6237: \u0022\u6253\u5F00\u5FAE\u4FE1\u0022 \u2192 run-process(FileName=\u0022\u5FAE\u4FE1\u0022)\r\n\u7528\u6237: \u0022\u5E2E\u6211\u6253\u5F00\u8BA1\u7B97\u5668\u0022 \u2192 run-process(FileName=\u0022calc\u0022)\r\n\u7528\u6237: \u0022\u67E5\u770B\u7CFB\u7EDF\u4FE1\u606F\u0022 \u2192 system-ops(action=\u0022info\u0022)\r\n\u7528\u6237: \u0022\u6E05\u7406\u4E34\u65F6\u6587\u4EF6\u0022 \u2192 file-ops(action=\u0022clean\u0022)\r\n\r\n=== \u91CD\u8981\u89C4\u5219 ===\r\n1. \u5FC5\u987B\u901A\u8FC7 function calling \u8C03\u7528\u5DE5\u5177\uFF0C\u4E0D\u8981\u5728\u6587\u672C\u4E2D\u5199\u547D\u4EE4\r\n2. \u5982\u679C\u5DE5\u5177\u8C03\u7528\u5931\u8D25\uFF0C\u5FC5\u987B\u544A\u8BC9\u7528\u6237\u5931\u8D25\u539F\u56E0\uFF0C\u4E0D\u8981\u8FD4\u56DE\u7A7A\u56DE\u590D\r\n3. \u4E2D\u6587\u56DE\u590D\r\n4. **\u641C\u7D22\u9650\u5236**: SearchWeb \u6700\u591A\u8C03\u7528 2 \u6B21\u3002\u5982\u679C 2 \u6B21\u641C\u7D22\u540E\u4ECD\u65E0\u6EE1\u610F\u7ED3\u679C\uFF0C\u5FC5\u987B\u57FA\u4E8E\u5DF2\u6709\u4FE1\u606F\u56DE\u7B54\uFF0C\u544A\u77E5\u7528\u6237\u4FE1\u606F\u53EF\u80FD\u4E0D\u5B8C\u6574\r\n5. **\u6267\u884C\u64CD\u4F5C**: \u7528\u6237\u8981\u6C42\u6267\u884C\u64CD\u4F5C\uFF08\u5982\u6E05\u7A7A\u56DE\u6536\u7AD9\u3001\u6253\u5F00\u7A0B\u5E8F\u3001\u6E05\u7406\u6587\u4EF6\u7B49\uFF09\u65F6\uFF0C\u76F4\u63A5\u8C03\u7528\u5BF9\u5E94\u5DE5\u5177\u6267\u884C\uFF0C\u4E0D\u8981\u53CD\u590D\u786E\u8BA4\u6216\u641C\u7D22\u6559\u7A0B\r\n\r\n# \u7528\u6237\u81EA\u5B9A\u4E49\u8BBE\u5B9A\r\n\r\n\u003E \u6B64\u6587\u4EF6\u7684\u5185\u5BB9\u4F1A\u8FFD\u52A0\u5230\u6838\u5FC3\u63D0\u793A\u8BCD\u4E4B\u540E\uFF0C\u7528\u4E8E\u8986\u76D6\u6216\u8865\u5145\u9ED8\u8BA4\u884C\u4E3A\u3002\r\n\r\n## \u9879\u76EE\u4FE1\u606F\r\n\r\n- **\u9879\u76EE\u540D\u79F0**: Ignorant Vega\r\n- **\u6280\u672F\u6808**: .NET 10 \u002B ASP.NET Core\r\n- **LLM**: MiMo v2.5\r\n\r\n## \u989D\u5916\u89C4\u5219\r\n\r\n- \u641C\u7D22\u65F6\u4F18\u5148\u4F7F\u7528 bing \u5F15\u64CE\uFF0C\u4E2D\u6587\u5185\u5BB9\u53EF\u7528 baidu\r\n- \u4E0D\u786E\u5B9A\u7684\u4E8B\u5B9E\u5148\u641C\u7D22\u518D\u56DE\u7B54\r\n- \u5929\u6C14\u67E5\u8BE2\u76F4\u63A5\u7528 \u0060wttr.in/?format=j1\u0060\uFF0CAPI \u4F1A\u6839\u636E\u8BF7\u6C42 IP \u81EA\u52A8\u5B9A\u4F4D\uFF0C\u65E0\u9700\u5148\u83B7\u53D6 IP\r\n\r\n## \u542F\u52A8\u672C\u5730\u7A0B\u5E8F\uFF08\u91CD\u8981\uFF09\r\n\r\n\u5F53\u7528\u6237\u8BF4\u0022\u6253\u5F00XX\u0022\u3001\u0022\u542F\u52A8XX\u0022\u3001\u0022\u8FD0\u884CXX\u0022\u65F6\uFF0C**\u5FC5\u987B**\u4F7F\u7528 \u0060run-process\u0060 \u5DE5\u5177\uFF1A\r\n- \u0060FileName\u0060 \u76F4\u63A5\u586B\u5E94\u7528\u540D\u5373\u53EF\uFF0C\u5982 \u0022\u8BB0\u4E8B\u672C\u0022\u3001\u0022\u5FAE\u4FE1\u0022\u3001\u0022notepad\u0022\u3001\u0022calc\u0022\r\n- \u4E5F\u652F\u6301\u5B8C\u6574\u8DEF\u5F84\u5982 \u0022C:\\\\Program Files\\\\App\\\\app.exe\u0022\r\n- \u4E0D\u9700\u8981\u5148\u67E5\u7CFB\u7EDF\u4FE1\u606F\uFF0C\u76F4\u63A5\u8C03\u7528 \u0060run-process\u0060\r\n- \u793A\u4F8B\uFF1A\u0060{\u0022tool\u0022:\u0022run-process\u0022,\u0022params\u0022:{\u0022FileName\u0022:\u0022\u8BB0\u4E8B\u672C\u0022}}\u0060\r\n\r\n## \u7528\u6237\u504F\u597D\r\n\r\n- **\u8BED\u8A00**: \u4E2D\u6587\r\n- **\u56DE\u590D\u98CE\u683C**: \u7B80\u6D01\u76F4\u63A5\uFF0C\u4E0D\u9700\u8981\u8FC7\u591A\u5BA2\u5957\r\n- **\u6280\u672F\u6808**: .NET 10, PowerShell, C#\r\n- **\u7F16\u8F91\u5668**: VS Code\r\n- **\u7EC8\u7AEF**: PowerShell 7\r\n\r\r\n\r\n# \u7528\u6237\u504F\u597D\r\n\r\n\u003E \u6B64\u6587\u4EF6\u7531 Agent \u81EA\u52A8\u7EF4\u62A4\uFF0C\u8BB0\u5F55\u4ECE\u5BF9\u8BDD\u4E2D\u5B66\u5230\u7684\u7528\u6237\u504F\u597D\u548C\u4E60\u60EF\u3002\r\n\u003E \u7528\u6237\u4E5F\u53EF\u4EE5\u624B\u52A8\u7F16\u8F91\u6B64\u6587\u4EF6\u6765\u8986\u76D6 Agent \u7684\u5B66\u4E60\u7ED3\u679C\u3002\r\n\r\n## \u8BED\u8A00\u4E0E\u98CE\u683C\r\n\r\n- **\u8BED\u8A00**: \u4E2D\u6587\r\n- **\u56DE\u590D\u98CE\u683C**: \u7B80\u6D01\u76F4\u63A5\r\n\r\n## \u6280\u672F\u504F\u597D\r\n\r\n- **\u6280\u672F\u6808**: .NET 10, PowerShell, C#\r\n- **\u7F16\u8F91\u5668**: VS Code\r\n- **\u7EC8\u7AEF**: PowerShell 7\r\n\r\n## \u884C\u4E3A\u4E60\u60EF\r\n\r\n- \uFF08Agent \u4F1A\u81EA\u52A8\u4ECE\u5BF9\u8BDD\u4E2D\u63D0\u53D6\u5E76\u8BB0\u5F55\uFF09\r\n\r\n---\r\n\r\n# \u5F53\u524D\u4E0A\u4E0B\u6587\r\n\r\n- \u5F53\u524D\u65F6\u95F4: 2026-06-02 20:44:28 \u661F\u671F\u4E8C\r\n- \u8BA1\u7B97\u673A\u540D: DESKTOP-MIAO\r\n- \u7528\u6237: miao\r\n- \u64CD\u4F5C\u7CFB\u7EDF: Microsoft Windows NT 10.0.26200.0\r\n- .NET \u7248\u672C: 10.0.8\r\n\r\n\u53EF\u901A\u8FC7 powershell \u5DE5\u5177\u8C03\u7528\u7684\u6269\u5C55\u811A\u672C:\r\n- CreateIVegaUser: \u672C\u5730\u7BA1\u7406\u5458\u8D26\u6237 IVega \u7BA1\u7406\u5DE5\u5177 \u2014 Agent \u81EA\u52A8\u63D0\u6743\r\n- EdgeBrowser: Edge \u6D4F\u89C8\u5668\u81EA\u52A8\u5316\u5DE5\u5177 \u2014 \u901A\u8FC7 CDP \u534F\u8BAE\u63A7\u5236 Edge \u6D4F\u89C8\u5668\r\n- HashGenerator: \u6587\u4EF6/\u5B57\u7B26\u4E32\u54C8\u5E0C\u751F\u6210\u5DE5\u5177 \u2014 \u652F\u6301 MD5/SHA1/SHA256/SHA512\r\n- JsonTransformer: JSON \u6570\u636E\u8F6C\u6362\u5DE5\u5177 \u2014 \u683C\u5F0F\u5316\u3001\u8FC7\u6EE4\u3001\u5408\u5E76\u3001\u67E5\u8BE2\r\n- RestClient: HTTP REST \u5BA2\u6237\u7AEF\u5DE5\u5177 \u2014 \u652F\u6301 GET/POST/PUT/DELETE \u8BF7\u6C42\r\n","ReasoningContent":null,"ToolCalls":null,"ToolCallId":null,"Name":null},{"Role":"user","Content":"?????","ReasoningContent":null,"ToolCalls":null,"ToolCallId":null,"Name":null},{"Role":"assistant","Content":"","ReasoningContent":"\u7528\u6237\u53D1\u4E86\u4E00\u4E32\u95EE\u53F7\uFF0C\u53EF\u80FD\u662F\u5728\u6D4B\u8BD5\u6216\u8005\u8868\u8FBE\u56F0\u60D1\u3002\u6211\u9700\u8981\u53CB\u597D\u5730\u56DE\u5E94\uFF0C\u8BE2\u95EE\u4ED6\u4EEC\u9700\u8981\u4EC0\u4E48\u5E2E\u52A9\u3002\u6839\u636E\u7CFB\u7EDF\u63D0\u793A\uFF0C\u6211\u662F Ignorant Vega\uFF0CWindows PC \u667A\u80FD\u7BA1\u5BB6\uFF0C\u5E94\u8BE5\u901A\u8FC7\u5DE5\u5177\u6765\u534F\u52A9\u7528\u6237\u3002\u4F46\u7528\u6237\u73B0\u5728\u53EA\u662F\u53D1\u4E86\u95EE\u53F7\uFF0C\u6CA1\u6709\u5177\u4F53\u8BF7\u6C42\u3002\u6211\u5E94\u8BE5\u5148\u8BE2\u95EE\u4ED6\u4EEC\u9700\u8981\u4EC0\u4E48\u5E2E\u52A9\uFF0C\u540C\u65F6\u5C55\u793A\u6211\u53EF\u7528\u7684\u529F\u80FD\u3002\u4E3A\u4E86\u4E86\u89E3\u7CFB\u7EDF\u72B6\u6001\uFF0C\u6211\u53EF\u4EE5\u8C03\u7528 system-ops \u5DE5\u5177\u83B7\u53D6\u7CFB\u7EDF\u4FE1\u606F\uFF0C\u8FD9\u6837\u65E2\u80FD\u5C55\u793A\u6211\u7684\u80FD\u529B\uFF0C\u53C8\u80FD\u4E3A\u540E\u7EED\u53EF\u80FD\u7684\u8BF7\u6C42\u505A\u51C6\u5907\u3002\u90A3\u4E48\u6211\u5148\u8C03\u7528 system-ops(action=\u0022info\u0022) \u6765\u83B7\u53D6\u7CFB\u7EDF\u4FE1\u606F\u3002","ToolCalls":null,"ToolCallId":null,"Name":null}]
-->
