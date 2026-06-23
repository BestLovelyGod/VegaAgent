// CreateIVegaUser.cs
// 本地管理员账户 IVega 管理工具 — Agent 自动提权
//
// 用法:
//   CreateIVegaUser <action> [--force]
//
// 操作:
//   Check            检查账户状态
//   Create           创建账户 (交互式输入密码)
//   Delete           删除账户和凭据
//   Disable          禁用账户 (保留凭据)
//   Enable           启用账户
//   StoreCredential  存储凭据 (交互式输入密码)
//
// 输出: JSON 格式结果
// 退出码: 0=成功, 1=错误, 2=需要管理员权限

using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

// ── 参数解析 ──────────────────────────────────────────────────────────────

if (args.Length == 0)
{
    Console.Error.WriteLine("用法: CreateIVegaUser <action> [--force]");
    Console.Error.WriteLine("  action: Check|Create|Delete|Disable|Enable|StoreCredential");
    Console.Error.WriteLine("  --force: 跳过确认提示 (Delete 操作)");
    Environment.Exit(1);
    return;
}

var action = args[0].ToLowerInvariant();
var force = args.Any(a => a.Equals("--force", StringComparison.OrdinalIgnoreCase));

var validActions = new[] { "check", "create", "delete", "disable", "enable", "storecredential" };
if (!validActions.Contains(action))
{
    Console.Error.WriteLine($"未知操作: {action}");
    Environment.Exit(1);
    return;
}

const string UserName = "IVega";
var credentialDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "IgnorantVega");
var credentialFile = Path.Combine(credentialDir, "ivega-cred.xml");

var result = new Result
{
    Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
    Action = action,
    UserName = UserName
};

// ── 主逻辑 ───────────────────────────────────────────────────────────────

try
{
    // 写入操作需要管理员权限
    var needsAdmin = action is "create" or "delete" or "disable" or "enable";
    if (needsAdmin && !IsAdmin())
    {
        result.Error = $"操作 '{action}' 需要管理员权限运行";
        Console.Error.WriteLine(result.Error);
        OutputResult(result, exitCode: 2);
        return;
    }

    var userExists = UserExists(UserName);
    var inAdminGroup = userExists ? UserInAdminGroup(UserName) : false;
    var credStored = File.Exists(credentialFile);

    result.UserExists = userExists;
    result.InAdminGroup = inAdminGroup;
    result.CredentialStored = credStored;

    switch (action)
    {
        case "check":
            HandleCheck(result, userExists, inAdminGroup, credStored);
            break;
        case "create":
            HandleCreate(result, userExists, inAdminGroup, credStored);
            break;
        case "delete":
            HandleDelete(result, userExists, inAdminGroup, force);
            break;
        case "disable":
            HandleDisable(result, userExists);
            break;
        case "enable":
            HandleEnable(result, userExists);
            break;
        case "storecredential":
            HandleStoreCredential(result, userExists);
            break;
    }
}
catch (Exception ex)
{
    result.Error = $"操作失败: {ex.Message}";
}

OutputResult(result, exitCode: result.Error != null ? 1 : 0);

// ── 操作处理 ──────────────────────────────────────────────────────────────

void HandleCheck(Result r, bool exists, bool inAdmin, bool credStored)
{
    if (!exists)
    {
        Console.WriteLine("[X] 用户 IVega 不存在");
        r.Status = "用户不存在";
        return;
    }

    var enabled = UserEnabled(UserName);
    Console.WriteLine($"[OK] 用户 IVega 已存在 (Enabled={enabled})");
    if (enabled) Console.WriteLine("[OK] 账户已启用");
    else { Console.WriteLine("[!] 账户已禁用"); r.Warnings.Add("账户已禁用"); }
    if (inAdmin) Console.WriteLine("[OK] 已在 Administrators 组");
    else { Console.WriteLine("[!] 不在 Administrators 组"); r.Warnings.Add("不在管理员组"); }
    if (credStored) Console.WriteLine("[OK] 凭据已存储");
    else { Console.WriteLine("[!] 凭据未存储"); r.Warnings.Add("凭据未存储"); }

    r.Status = "用户存在";
    r.Enabled = enabled;
}

void HandleCreate(Result r, bool exists, bool inAdmin, bool credStored)
{
    ShowSecurityWarning();

    SecureString? password = null;

    if (exists)
    {
        Console.WriteLine("[i] 用户 IVega 已存在");
        if (!inAdmin)
        {
            RunNet($"localgroup Administrators {UserName} /add");
            Console.WriteLine("[OK] 已添加到 Administrators 组");
        }
        if (!credStored)
        {
            Console.WriteLine("[i] 需要存储凭据");
            if (force)
            {
                password = GenerateRandomPassword();
                Console.WriteLine("[OK] 已自动生成随机密码");
            }
            else
            {
                password = ReadPassword("请输入 IVega 的密码");
            }
        }
    }
    else
    {
        Console.WriteLine("[>] 正在创建用户 IVega...");
        if (force)
        {
            password = GenerateRandomPassword();
            Console.WriteLine("[OK] 已自动生成随机密码");
        }
        else
        {
            password = ReadPassword("请输入密码");
            var confirm = ReadPassword("请再次输入密码");
            if (!SecureStringEquals(password, confirm))
            {
                r.Error = "密码不一致";
                Console.WriteLine("[X] 密码不一致");
                return;
            }
            if (password.Length < 8)
            {
                r.Error = "密码不足8位";
                Console.WriteLine("[X] 密码不足8位");
                return;
            }
        }

        // 创建用户
        var plainPwd = SecureStringToString(password);
        var result1 = RunNet($"user {UserName} \"{plainPwd}\" /add /passwordchg:no /expires:never");
        if (result1.ExitCode != 0)
        {
            r.Error = $"创建用户失败: {result1.Output}";
            Console.WriteLine($"[X] {r.Error}");
            return;
        }
        Console.WriteLine("[OK] 用户 IVega 创建成功");

        // 添加到管理员组
        RunNet($"localgroup Administrators {UserName} /add");
        Console.WriteLine("[OK] 已添加到 Administrators 组");

        // 设置密码永不过期
        RunNet($"user {UserName} /expires:never");
        RunNet($"user {UserName} /passwordchg:no");

        r.Status = "用户创建成功";
    }

    if (password != null)
    {
        SaveCredential(password);
        r.CredentialStored = true;
    }

    ShowUsageNotes();
}

void HandleDelete(Result r, bool exists, bool inAdmin, bool forceMode)
{
    if (!exists)
    {
        DeleteCredentialFile();
        r.Status = "用户不存在，已清理凭据";
        Console.WriteLine("[i] 用户不存在，已清理凭据");
        return;
    }

    if (!forceMode)
    {
        Console.Write("确认删除 IVega 账户和凭据? (输入 YES): ");
        var input = Console.ReadLine();
        if (input != "YES")
        {
            r.Status = "取消";
            Console.WriteLine("[X] 取消");
            return;
        }
    }

    DeleteCredentialFile();
    if (inAdmin) RunNet($"localgroup Administrators {UserName} /delete");
    RunNet($"user {UserName} /delete");
    r.Status = "已删除";
    Console.WriteLine("[OK] 账户和凭据已删除");
}

void HandleDisable(Result r, bool exists)
{
    if (!exists) { r.Error = "用户 IVega 不存在"; Console.WriteLine($"[X] {r.Error}"); return; }

    RunNet($"user {UserName} /active:no");
    r.Status = "已禁用";
    Console.WriteLine("[OK] 账户 IVega 已禁用 (凭据保留, 可随时 Enable 恢复)");
}

void HandleEnable(Result r, bool exists)
{
    if (!exists) { r.Error = "用户 IVega 不存在"; Console.WriteLine($"[X] {r.Error}"); return; }

    RunNet($"user {UserName} /active:yes");
    r.Status = "已启用";
    Console.WriteLine("[OK] 账户 IVega 已启用");
    if (!File.Exists(credentialFile))
        Console.WriteLine("[!] 凭据未存储, 请运行 StoreCredential");
}

void HandleStoreCredential(Result r, bool exists)
{
    if (!exists) { r.Error = "用户 IVega 不存在"; Console.WriteLine($"[X] {r.Error}"); return; }

    SecureString password;
    if (force)
    {
        password = GenerateRandomPassword();
        Console.WriteLine("[OK] 已自动生成随机密码");
    }
    else
    {
        password = ReadPassword("请输入 IVega 的密码");
    }
    SaveCredential(password);
    r.CredentialStored = true;
    r.Status = "凭据已存储";
}

// ── 辅助方法 ──────────────────────────────────────────────────────────────

static bool IsAdmin()
{
    using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
    var principal = new System.Security.Principal.WindowsPrincipal(identity);
    return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
}

static bool UserExists(string name)
{
    var r = RunNet($"user {name}");
    return r.ExitCode == 0 && r.Output.Contains(name, StringComparison.OrdinalIgnoreCase);
}

static bool UserEnabled(string name)
{
    var r = RunNet($"user {name}");
    if (r.ExitCode != 0) return false;
    // "Account active               Yes"
    if (r.Output.Contains("Account active", StringComparison.OrdinalIgnoreCase))
        return r.Output.Contains("Account active               Yes", StringComparison.OrdinalIgnoreCase);
    return true;
}

static bool UserInAdminGroup(string name)
{
    var r = RunNet("localgroup Administrators");
    return r.ExitCode == 0 && r.Output.Contains(name, StringComparison.OrdinalIgnoreCase);
}

static (int ExitCode, string Output) RunNet(string arguments)
{
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = "net",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi);
        if (p == null) return (-1, "");
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(10000);
        return (p.ExitCode, output);
    }
    catch (Exception ex)
    {
        return (-1, ex.Message);
    }
}

static SecureString ReadPassword(string prompt)
{
    Console.Write($"{prompt}: ");
    var pwd = new SecureString();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); break; }
        if (key.Key == ConsoleKey.Backspace && pwd.Length > 0) { pwd.RemoveAt(pwd.Length - 1); Console.Write("\b \b"); }
        else if (key.Key != ConsoleKey.Backspace) { pwd.AppendChar(key.KeyChar); Console.Write("*"); }
    }
    return pwd;
}

static bool SecureStringEquals(SecureString a, SecureString b)
{
    var sa = Marshal.PtrToStringUni(Marshal.SecureStringToBSTR(a));
    var sb = Marshal.PtrToStringUni(Marshal.SecureStringToBSTR(b));
    return sa == sb;
}

static string SecureStringToString(SecureString ss)
{
    var ptr = Marshal.SecureStringToBSTR(ss);
    try { return Marshal.PtrToStringUni(ptr) ?? ""; }
    finally { Marshal.ZeroFreeBSTR(ptr); }
}

static SecureString GenerateRandomPassword(int length = 24)
{
    const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    const string lower = "abcdefghjkmnpqrstuvwxyz";
    const string digits = "23456789";
    const string symbols = "!@#$%&*+-=";
    var all = upper + lower + digits + symbols;
    var bytes = RandomNumberGenerator.GetBytes(length);
    var pwd = new char[length];
    // Ensure at least one of each category
    pwd[0] = upper[bytes[0] % upper.Length];
    pwd[1] = lower[bytes[1] % lower.Length];
    pwd[2] = digits[bytes[2] % digits.Length];
    pwd[3] = symbols[bytes[3] % symbols.Length];
    for (int i = 4; i < length; i++) pwd[i] = all[bytes[i] % all.Length];
    // Shuffle
    for (int i = length - 1; i > 0; i--)
    {
        int j = bytes[i] % (i + 1);
        (pwd[i], pwd[j]) = (pwd[j], pwd[i]);
    }
    var ss = new SecureString();
    foreach (var c in pwd) ss.AppendChar(c);
    Array.Clear(pwd);
    return ss;
}

void SaveCredential(SecureString password)
{
    if (!Directory.Exists(credentialDir))
        Directory.CreateDirectory(credentialDir);

    // Export as DPAPI-encrypted XML (compatible with PowerShell Import-Clixml)
    var plain = SecureStringToString(password);
    var secure = new NetworkCredential("", plain).SecurePassword;
    var encrypted = ProtectedData.Protect(
        Encoding.Unicode.GetBytes(plain + "\0"), null, DataProtectionScope.CurrentUser);

    // Write PowerShell-compatible Clixml format
    var xml = $@"<Objs Version=""1.1.0.1"" xmlns=""http://schemas.microsoft.com/powershell/2004/04""><Obj RefId=""0""><TN RefId=""0""><T>System.Security.SecureString</T><T>System.Object</T></TN><MS><SS>{Convert.ToBase64String(encrypted)}</SS></MS></Obj></Objs>";
    File.WriteAllText(credentialFile, xml);

    // Restrict ACL
    var acl = System.IO.FileSystemAclExtensions.GetAccessControl(new FileInfo(credentialFile));
    acl.SetAccessRuleProtection(true, false);
    acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule("BUILTIN\\Administrators", System.Security.AccessControl.FileSystemRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
    acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule("NT AUTHORITY\\SYSTEM", System.Security.AccessControl.FileSystemRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
    System.IO.FileSystemAclExtensions.SetAccessControl(new FileInfo(credentialFile), acl);

    Console.WriteLine($"[OK] 凭据已加密存储 (DPAPI): {credentialFile}");
}

void DeleteCredentialFile()
{
    if (File.Exists(credentialFile))
    {
        File.Delete(credentialFile);
        Console.WriteLine("[OK] 已删除凭据文件");
    }
}

void ShowSecurityWarning()
{
    Console.WriteLine();
    Console.WriteLine("================================================================");
    Console.WriteLine("                    WARNING - Security Notice                   ");
    Console.WriteLine("================================================================");
    Console.WriteLine("  即将创建管理员账户 IVega，该账户拥有以下权限:");
    Console.WriteLine("  [!] 删除系统文件、修改注册表、管理用户、访问所有文件");
    Console.WriteLine("  [!] Agent 会自动使用此账户绕过 UAC 执行提权操作");
    Console.WriteLine("  [+] 建议: 使用强密码, 定期更换, 监控审计日志");
    Console.WriteLine("================================================================");
    Console.WriteLine();
}

void ShowUsageNotes()
{
    Console.WriteLine();
    Console.WriteLine("=== Agent 自动提权已配置 ===");
    Console.WriteLine($"  凭据存储: {credentialFile}");
    Console.WriteLine("  Agent 遇到权限不足时将自动使用 IVega 提权执行");
    Console.WriteLine("  管理: Disable(禁用) / Enable(启用) / Delete(删除)");
    Console.WriteLine();
}

void OutputResult(Result r, int exitCode)
{
    var json = JsonSerializer.Serialize(r, ResultJsonContext.Default.Result);
    Console.WriteLine(json);
    // 输出错误到 stderr 让 DotnetScriptTool 捕获
    if (r.Error != null)
        Console.Error.WriteLine(r.Error);
    Environment.Exit(exitCode);
}

// ── 数据模型 ──────────────────────────────────────────────────────────────

class Result
{
    [JsonPropertyName("timestamp")] public string Timestamp { get; set; } = "";
    [JsonPropertyName("action")] public string Action { get; set; } = "";
    [JsonPropertyName("userName")] public string UserName { get; set; } = "";
    [JsonPropertyName("userExists")] public bool UserExists { get; set; }
    [JsonPropertyName("inAdminGroup")] public bool InAdminGroup { get; set; }
    [JsonPropertyName("credentialStored")] public bool CredentialStored { get; set; }
    [JsonPropertyName("enabled")] public bool? Enabled { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("warnings")] public List<string> Warnings { get; set; } = new();
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Result))]
partial class ResultJsonContext : JsonSerializerContext { }
