// ============================================================================
// Agent.TUI 入口点 (Terminal.Gui v2)
// ============================================================================

// CS0618: Terminal.Gui v2 弃用 API 抑制已移至 csproj <NoWarn>

using Terminal.Gui.App;
using Agent.TUI.Services;
using Agent.TUI.Views;

var baseUrl = args.Length > 0 ? args[0] : Agent.Core.Config.AppConstants.DefaultBaseUrl;

Console.WriteLine("╔══════════════════════════════════════════════╗");
Console.WriteLine("║       Ignorant Vega TUI v1.2.0              ║");
Console.WriteLine("║       织女星 — 终端交互界面                 ║");
Console.WriteLine("╠══════════════════════════════════════════════╣");
Console.WriteLine($"║  Agent Host: {baseUrl,-32} ║");
Console.WriteLine("╚══════════════════════════════════════════════╝");
Console.WriteLine();

// 检查连接
using var agent = new TuiAgentService(baseUrl);

Console.Write("正在连接 Agent Host... ");
var isOnline = await agent.IsOnlineAsync();
if (isOnline)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("✓ 已连接");
}
else
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("✗ 未连接 (离线模式)");
    Console.ResetColor();
    Console.WriteLine();
    Console.WriteLine("提示: 请先启动 Agent.Host:");
    Console.WriteLine("  cd src/Agent.Host && dotnet run");
    Console.WriteLine();
    Console.WriteLine("按任意键继续 (离线模式)...");
    Console.ReadKey(true);
}
Console.ResetColor();

// 启动 TUI
Application.Init();
try
{
    var mainWindow = new MainWindow(agent);
    Application.Run(mainWindow);
    mainWindow.Dispose();
}
finally
{
    Application.Shutdown();
}

Console.WriteLine("再见！👋");
