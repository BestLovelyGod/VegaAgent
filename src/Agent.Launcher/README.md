# Vega 启动器

一个基于 WinForms 的图形化启动器，用于管理 Vega 项目的各个组件和环境。

## 设计理念

```
用户双击 launcher.cmd → 启动器立即打开 → 检测环境状态 → 用户点击安装 → 启动服务
```

启动器本身是**自包含的独立 EXE**，不需要预装 .NET SDK 即可运行。**启动器不会自动下载任何东西**，用户看到界面后自己决定何时安装环境。

## 功能特性

### 🚀 服务管理
- **启动核心服务** - 通过 NSSM 启动/停止 Agent.Host Windows 服务
- **打开对话界面** - 优先使用内置 SDK 以 `dotnet Agent.GUI.dll` 启动（无需系统安装 .NET Runtime），回退到 `Agent.GUI.exe`
- **设置按钮** - 配置 LLM 模型、API Key、启动选项

### 🔧 环境管理
- 启动时只检测环境状态（.NET SDK + WebView2 + 浏览器插件 + IVega 账户），不自动安装
- 环境缺失时显示「安装环境」按钮，用户点击后才开始安装
- **安装后以实际文件/注册表为准**验证成功，不依赖脚本退出码（避免 aria2c 误报）
- 启动服务时如果环境未就绪，提示用户先安装

### ⚙️ 设置对话框
- **LLM 配置**: 提供商选择（MiMo / MiMo Token Plan）、模型切换、API Key 管理（密码掩码 + 测试连接）
- **启动选项**: 自动启动 Host、自动启动 GUI、开机自启动
- **界面选项**: 关闭时最小化到系统托盘
- 配置保存到 `%APPDATA%/Vega/launcher-config.json` 和 `data/llm-config.json`

### 🔐 IVega 账户管理
- 自动创建 IVega 提权账户（强密码、管理员组、加密凭据存储）
- **自动从 Windows 登录界面隐藏**（通过注册表 `SpecialAccounts\UserList`）
- 创建后自动重试验证（SAM 数据库同步需要时间）

### 📊 日志记录
- 实时操作日志显示
- 文件日志记录（按日期分文件）
- 自动清理旧日志（保留7天）

## 使用方法

### 快速启动（推荐）
```cmd
REM 双击即可，无需预装任何环境
launcher.cmd
```

### 发布模式
```bash
REM 发布为自包含 EXE（无需 .NET Runtime）
dotnet publish src/Agent.Launcher/Agent.Launcher.csproj -c Release --self-contained -r win-x64 -o publish/release/Agent.Launcher
```

## 界面说明

### 主界面
- **环境状态区** - 显示 .NET SDK、WebView2、浏览器插件、IVega 账户的状态
- **安装环境按钮** - 环境缺失时显示，一键安装所有依赖
- **启动核心服务** - 通过 NSSM 管理 Agent.Host Windows 服务
- **打开对话界面** - 启动/关闭 Agent.GUI（监听进程退出自动刷新按钮状态）
- **设置按钮** - 打开 LLM 配置和启动选项
- **操作日志** - 显示实时操作记录

### 设置对话框
- LLM 提供商/模型选择 + API Key 管理 + 测试连接
- 启动选项（自动启动、开机自启）
- 界面选项（最小化到托盘）

## 配置文件

启动器配置：
```
%APPDATA%/Vega/launcher-config.json
```

LLM 配置（与 GUI 共享）：
```
data/llm-config.json
```

日志文件：
```
%APPDATA%/Vega/logs/launcher-YYYY-MM-DD.log
```

## 技术细节

### GUI 启动策略
1. 优先: 内置 SDK (`dotnet.exe`) + `Agent.GUI.dll`（`CreateNoWindow=true`，无命令行弹窗）
2. 回退: 直接运行 `Agent.GUI.exe`（需要系统安装 .NET Desktop Runtime）

### 环境检测策略
- **SDK**: 检查 `Agent.Host/sdk/dotnet/dotnet.exe` 是否存在
- **WebView2**: 5 种检测方式（HKLM 64位/32位、HKCU 注册表、exe 文件、安装目录）
- **IVega**: 运行 `Create-IVegaUser.ps1 -Action Check`，从混合输出中提取 JSON

### 服务管理
- 使用 NSSM (Non-Sucking Service Manager) 管理 Agent.Host 为 Windows 服务
- 服务名称: `VegaAgent`，手动启动模式
- 日志输出重定向到 `data/logs/host-stdout.log` 和 `host-stderr.log`

## 注意事项

1. **管理员权限** - 启动器自动请求管理员权限（UAC）
2. **端口冲突** - 确保 7300 端口未被其他程序占用
3. **防火墙** - 首次运行时可能需要防火墙放行
4. **杀毒软件** - 某些杀毒软件可能误报，需要添加信任

## 故障排除

### 启动器无法启动
1. 确认是 Windows 10/11 x64 系统
2. 以管理员身份运行
3. 检查 `%APPDATA%/Vega/logs/` 下的日志文件

### SDK 安装失败
1. 检查网络连接（需要下载约 300MB）
2. 检查磁盘空间（需要约 500MB）
3. 手动下载: https://dotnet.microsoft.com/download/dotnet/10.0

### GUI 无法启动
1. 启动器会自动使用内置 SDK 启动 GUI，无需安装 .NET Runtime
2. 如果提示 WebView2 未安装，点击「安装环境」自动安装
3. 检查 `data/llm-config.json` 中的 API Key 是否已配置
1. 检查端口是否被占用
2. 查看日志文件获取详细错误
3. 手动运行服务测试

### 界面显示异常
1. 更新显卡驱动
2. 检查 DPI 设置
3. 重置窗口配置

## 开发说明

### 项目结构
```
src/Agent.Launcher/
├── Agent.Launcher.csproj    # 项目文件
├── Program.cs               # 入口点
├── MainForm.cs              # 主窗体逻辑
├── MainForm.Designer.cs     # 主窗体设计
├── SettingsForm.cs          # 设置对话框
├── SettingsForm.Designer.cs # 设置对话框设计
├── LauncherConfig.cs        # 配置管理
├── Logger.cs                # 日志记录
└── app.manifest             # 应用清单
```

### 扩展开发
1. 添加新的服务管理
2. 扩展环境配置选项
3. 集成更多管理工具
4. 添加监控和报警功能

## 许可证

本项目遵循 Vega 项目许可证。
