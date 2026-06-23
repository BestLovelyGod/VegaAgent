using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows.Forms;

namespace Agent.Launcher;

static class Program
{
    [STAThread]
    static void Main()
    {
        // 自动提权：如果不是管理员，以管理员身份重新启动
        if (!IsRunAsAdmin())
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    Verb = "RunAs",
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex) { Debug.WriteLine($"[Launcher] 提权重启失败: {ex.Message}"); }
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }

    private static bool IsRunAsAdmin()
    {
        try
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex) { Debug.WriteLine($"[Launcher] 管理员权限检查失败: {ex.Message}"); return false; }
    }
}
