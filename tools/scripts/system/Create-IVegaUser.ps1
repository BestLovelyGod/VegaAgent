# Create-IVegaUser.ps1
# 创建本地管理员用户 IVega — 用于 Agent 自动提权
# 参数: Action(操作: Check/Create/Delete/Disable/Enable/StoreCredential)
#
# 注意: 此脚本需要以管理员权限运行 (Check 和 StoreCredential 除外)
#
# 与 CreateIVegaUser.cs 的区别:
#   - PS1 版本: 手动交互式使用 (彩色输出、友好提示)
#   - CS 版本:  Agent 自动调用 (正确退出码、stderr 错误捕获)
#   - 两者功能完全一致，数据互通

param(
    [ValidateSet("Check", "Create", "Delete", "Disable", "Enable", "StoreCredential")]
    [string]$Action = "Check",
    [switch]$Force
)

$script:CredentialDir = Join-Path $env:ProgramData "IgnorantVega"
$script:CredentialFile = Join-Path $script:CredentialDir "ivega-cred.xml"

function Show-SecurityWarning {
    Write-Host ""
    Write-Host "================================================================" -ForegroundColor Yellow
    Write-Host "                    WARNING - Security Notice                   " -ForegroundColor Yellow
    Write-Host "================================================================" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  即将创建管理员账户 IVega，该账户拥有以下权限:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  [!] 破坏性操作能力:" -ForegroundColor Red
    Write-Host "      - 删除系统文件和关键数据" -ForegroundColor Red
    Write-Host "      - 修改系统注册表" -ForegroundColor Red
    Write-Host "      - 安装/卸载软件和服务" -ForegroundColor Red
    Write-Host "      - 管理其他用户账户" -ForegroundColor Red
    Write-Host "      - 访问所有文件和目录" -ForegroundColor Red
    Write-Host ""
    Write-Host "  [!] 风险提示:" -ForegroundColor Yellow
    Write-Host "      - Agent 会自动使用此账户绕过 UAC 执行提权操作" -ForegroundColor Yellow
    Write-Host "      - 恶意工具调用可能导致系统损坏" -ForegroundColor Yellow
    Write-Host "      - 建议仅在可信环境中使用" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  [+] 安全建议:" -ForegroundColor Green
    Write-Host "      - 使用强密码 (至少12位，大小写+数字+符号)" -ForegroundColor Green
    Write-Host "      - 定期更换密码并运行 StoreCredential" -ForegroundColor Green
    Write-Host "      - 监控 Agent 的工具调用日志" -ForegroundColor Green
    Write-Host ""
    Write-Host "================================================================" -ForegroundColor Yellow
    Write-Host ""
}

function Show-UsageNotes {
    Write-Host ""
    Write-Host "=== Agent 自动提权已配置 ===" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  凭据存储: $script:CredentialFile" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  Agent 工作流程:" -ForegroundColor White
    Write-Host "    1. 工具执行遇到权限不足" -ForegroundColor White
    Write-Host "    2. 检测到 IVega 账户存在且凭据已存储" -ForegroundColor White
    Write-Host "    3. 以 IVega 身份重新执行命令" -ForegroundColor White
    Write-Host "    4. 所有提权操作记录审计日志" -ForegroundColor White
    Write-Host ""
    Write-Host "  管理命令:" -ForegroundColor White
    Write-Host "    - Check            查看账户状态" -ForegroundColor Gray
    Write-Host "    - Disable          禁用账户 (推荐, 快速恢复)" -ForegroundColor Gray
    Write-Host "    - Enable           启用账户" -ForegroundColor Gray
    Write-Host "    - StoreCredential  重新存储凭据" -ForegroundColor Gray
    Write-Host "    - Delete           删除账户和凭据 (不可恢复)" -ForegroundColor Gray
    Write-Host ""
}

function Test-AdminPrivilege {
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-UserExists {
    param([string]$UserName)
    try { $null = Get-LocalUser -Name $UserName -ErrorAction Stop; return $true }
    catch { return $false }
}

function Test-UserInAdminGroup {
    param([string]$UserName)
    try {
        $admins = Get-LocalGroupMember -Group "Administrators" -ErrorAction Stop
        return ($admins | Where-Object { $_.Name -like "*\$UserName" }) -ne $null
    } catch { return $false }
}

function Test-CredentialStored { return (Test-Path $script:CredentialFile) }

function Hide-UserFromLogonScreen {
    param([string]$UserName)
    $regPath = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\SpecialAccounts\UserList"
    if (-not (Test-Path $regPath)) {
        New-Item -Path $regPath -Force | Out-Null
    }
    New-ItemProperty -Path $regPath -Name $UserName -Value 0 -PropertyType DWORD -Force | Out-Null
}

function Test-HiddenFromLogonScreen {
    param([string]$UserName)
    $regPath = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\SpecialAccounts\UserList"
    try {
        $val = Get-ItemProperty -Path $regPath -Name $UserName -ErrorAction Stop
        return ($val.$UserName -eq 0)
    } catch { return $false }
}

function New-RandomPassword {
    # 生成 24 位随机强密码: 大小写字母 + 数字 + 特殊字符
    $upper = 'ABCDEFGHJKLMNPQRSTUVWXYZ'
    $lower = 'abcdefghjkmnpqrstuvwxyz'
    $digits = '23456789'
    $symbols = '!@#$%&*+-='
    $all = $upper + $lower + $digits + $symbols
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $bytes = New-Object byte[] 24
    $rng.GetBytes($bytes)
    $pwd = @(
        $upper[$bytes[0] % $upper.Length]
        $lower[$bytes[1] % $lower.Length]
        $digits[$bytes[2] % $digits.Length]
        $symbols[$bytes[3] % $symbols.Length]
    )
    for ($i = 4; $i -lt 24; $i++) { $pwd += $all[$bytes[$i] % $all.Length] }
    # 打乱顺序
    for ($i = $pwd.Length - 1; $i -gt 0; $i--) {
        $j = $bytes[$i] % ($i + 1)
        $pwd[$i], $pwd[$j] = $pwd[$j], $pwd[$i]
    }
    return (-join $pwd)
}

function Save-Credential {
    param([System.Security.SecureString]$SecurePassword)
    if (-not (Test-Path $script:CredentialDir)) {
        New-Item -ItemType Directory -Path $script:CredentialDir -Force | Out-Null
    }
    $SecurePassword | Export-Clixml -Path $script:CredentialFile
    $acl = Get-Acl $script:CredentialFile
    $acl.SetAccessRuleProtection($true, $false)
    $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule("BUILTIN\Administrators","FullControl","Allow")))
    $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule("NT AUTHORITY\SYSTEM","FullControl","Allow")))
    Set-Acl -Path $script:CredentialFile -AclObject $acl
    Write-Host "[OK] 凭据已加密存储 (DPAPI): $script:CredentialFile" -ForegroundColor Green
}

function Remove-StoredCredential {
    if (Test-Path $script:CredentialFile) {
        Remove-Item $script:CredentialFile -Force
        Write-Host "[OK] 已删除存储的凭据" -ForegroundColor Green
    }
}

# --- Main ---

$result = [ordered]@{
    Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Action = $Action; UserName = "IVega"
    UserExists = $false; InAdminGroup = $false; CredentialStored = $false; HiddenFromLogon = $false
    Status = $null; Error = $null; Warnings = @()
}

try {
    # 只有写入操作需要管理员权限
    $needsAdmin = $Action -in @("Create", "Delete", "Disable", "Enable")
    if ($needsAdmin -and -not (Test-AdminPrivilege)) {
        $result.Error = "操作 '$Action' 需要管理员权限运行"
        Write-Host "[X] $($result.Error)" -ForegroundColor Red
        $result | ConvertTo-Json -Depth 5; return
    }

    $userExists = Test-UserExists -UserName "IVega"
    $inAdminGroup = if ($userExists) { Test-UserInAdminGroup -UserName "IVega" } else { $false }
    $credStored = Test-CredentialStored
    $hiddenFromLogon = Test-HiddenFromLogonScreen -UserName "IVega"
    $result.UserExists = $userExists
    $result.InAdminGroup = $inAdminGroup
    $result.CredentialStored = $credStored
    $result.HiddenFromLogon = $hiddenFromLogon

    switch ($Action) {
        "Check" {
            if ($userExists) {
                $user = Get-LocalUser -Name "IVega"
                $enabled = $user.Enabled
                Write-Host "[OK] 用户 IVega 已存在" -ForegroundColor Green
                if ($enabled) { Write-Host "[OK] 账户已启用" -ForegroundColor Green }
                else { Write-Host "[!] 账户已禁用 (Agent 无法提权)" -ForegroundColor Yellow; $result.Warnings += "账户已禁用" }
                if ($inAdminGroup) { Write-Host "[OK] 已在 Administrators 组" -ForegroundColor Green }
                else { Write-Host "[!] 不在 Administrators 组" -ForegroundColor Yellow; $result.Warnings += "不在管理员组" }
                if ($credStored) { Write-Host "[OK] 凭据已存储 (可自动提权)" -ForegroundColor Green }
                else { Write-Host "[!] 凭据未存储 (无法自动提权)" -ForegroundColor Yellow; $result.Warnings += "凭据未存储" }
                if ($hiddenFromLogon) { Write-Host "[OK] 已从登录界面隐藏" -ForegroundColor Green }
                else { Write-Host "[!] 未从登录界面隐藏" -ForegroundColor Yellow; $result.Warnings += "未从登录界面隐藏" }
                $result.Status = "用户存在"
                $result | Add-Member -NotePropertyName "Enabled" -NotePropertyValue $enabled -Force
            } else {
                Write-Host "[X] 用户 IVega 不存在" -ForegroundColor Red
                Write-Host "    运行: Create-IVegaUser.ps1 -Action Create" -ForegroundColor Yellow
                $result.Status = "用户不存在"
            }
        }
        "Create" {
            Show-SecurityWarning
            $securePassword = $null
            if ($userExists) {
                Write-Host "[i] 用户 IVega 已存在" -ForegroundColor Cyan
                if (-not $inAdminGroup) {
                    Add-LocalGroupMember -Group "Administrators" -Member "IVega" -ErrorAction Stop
                    Write-Host "[OK] 已添加到 Administrators 组" -ForegroundColor Green
                }
                if (-not $credStored) {
                    Write-Host "[i] 需要存储凭据供 Agent 提权使用" -ForegroundColor Cyan
                    if ($Force) {
                        $plainPwd = New-RandomPassword
                        Write-Host "[OK] 已自动生成随机密码" -ForegroundColor Green
                    } else {
                        $plainPwd = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
                            [Runtime.InteropServices.Marshal]::SecureStringToBSTR(
                                (Read-Host -Prompt "请输入 IVega 的密码" -AsSecureString)))
                    }
                    $securePassword = ConvertTo-SecureString $plainPwd -AsPlainText -Force
                }
            } else {
                Write-Host "[>] 正在创建用户 IVega..." -ForegroundColor Yellow
                if ($Force) {
                    $plainPwd = New-RandomPassword
                    $securePassword = ConvertTo-SecureString $plainPwd -AsPlainText -Force
                    Write-Host "[OK] 已自动生成随机密码" -ForegroundColor Green
                } else {
                    $securePassword = Read-Host -Prompt "请输入密码" -AsSecureString
                    $confirmPassword = Read-Host -Prompt "请再次输入密码" -AsSecureString
                    $bstr1 = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
                    $bstr2 = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($confirmPassword)
                    $p1 = [Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr1)
                    $p2 = [Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr2)
                    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr1)
                    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr2)
                    if ($p1 -ne $p2) { $result.Error = "密码不一致"; Write-Host "[X] 密码不一致" -ForegroundColor Red; $result | ConvertTo-Json -Depth 5; return }
                    if ($p1.Length -lt 8) { $result.Error = "密码不足8位"; Write-Host "[X] 密码不足8位" -ForegroundColor Red; $result | ConvertTo-Json -Depth 5; return }
                }
                New-LocalUser -Name "IVega" -Password $securePassword -FullName "IVega Agent Account" -Description "Ignorant Vega 自动提权账户" -PasswordNeverExpires -AccountNeverExpires -ErrorAction Stop
                Write-Host "[OK] 用户 IVega 创建成功" -ForegroundColor Green
                Add-LocalGroupMember -Group "Administrators" -Member "IVega" -ErrorAction Stop
                Write-Host "[OK] 已添加到 Administrators 组" -ForegroundColor Green
                $result.Status = "用户创建成功"
            }
            if ($securePassword) { Save-Credential -SecurePassword $securePassword; $result.CredentialStored = $true }
            Hide-UserFromLogonScreen -UserName "IVega"
            Write-Host "[OK] 已从登录界面隐藏" -ForegroundColor Green
            $result.HiddenFromLogon = $true
            Show-UsageNotes
        }
        "StoreCredential" {
            if (-not $userExists) { $result.Error = "用户 IVega 不存在"; Write-Host "[X] $($result.Error)" -ForegroundColor Red; $result | ConvertTo-Json -Depth 5; return }
            if ($Force) {
                $plainPwd = New-RandomPassword
                Write-Host "[OK] 已自动生成随机密码" -ForegroundColor Green
            } else {
                $plainPwd = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
                    [Runtime.InteropServices.Marshal]::SecureStringToBSTR(
                        (Read-Host -Prompt "请输入 IVega 的密码" -AsSecureString)))
            }
            $securePassword = ConvertTo-SecureString $plainPwd -AsPlainText -Force
            Save-Credential -SecurePassword $securePassword
            $result.CredentialStored = $true; $result.Status = "凭据已存储"
        }
        "Disable" {
            if (-not $userExists) { $result.Error = "用户 IVega 不存在"; Write-Host "[X] $($result.Error)" -ForegroundColor Red; $result | ConvertTo-Json -Depth 5; return }
            Disable-LocalUser -Name "IVega" -ErrorAction Stop
            Write-Host "[OK] 账户 IVega 已禁用 (凭据保留, 可随时 Enable 恢复)" -ForegroundColor Green
            $result.Status = "已禁用"
        }
        "Enable" {
            if (-not $userExists) { $result.Error = "用户 IVega 不存在"; Write-Host "[X] $($result.Error)" -ForegroundColor Red; $result | ConvertTo-Json -Depth 5; return }
            Enable-LocalUser -Name "IVega" -ErrorAction Stop
            Write-Host "[OK] 账户 IVega 已启用" -ForegroundColor Green
            if (-not $credStored) { Write-Host "[!] 凭据未存储, 请运行 StoreCredential" -ForegroundColor Yellow }
            $result.Status = "已启用"
        }
        "Delete" {
            if (-not $userExists) { Remove-StoredCredential; $result.Status = "用户不存在"; Write-Host "[i] 用户不存在，已清理凭据" -ForegroundColor Cyan }
            else {
                $proceed = $false
                if ($Force) {
                    $proceed = $true
                } else {
                    $confirm = Read-Host -Prompt "确认删除 IVega 账户和凭据? (输入 YES)"
                    $proceed = ($confirm -eq "YES")
                }
                if ($proceed) {
                    Remove-StoredCredential
                    if ($inAdminGroup) { Remove-LocalGroupMember -Group "Administrators" -Member "IVega" -EA SilentlyContinue }
                    Remove-LocalUser -Name "IVega" -ErrorAction Stop
                    Write-Host "[OK] 账户和凭据已删除" -ForegroundColor Green
                    $result.Status = "已删除"
                } else { Write-Host "[X] 取消" -ForegroundColor Yellow; $result.Status = "取消" }
            }
        }
    }
} catch {
    $result.Error = "操作失败: $($_.Exception.Message)"
    Write-Host "[X] $($result.Error)" -ForegroundColor Red
}

$result | ConvertTo-Json -Depth 5

# 有错误时抛出异常 (让 PowerShellTool 检测到 HadErrors)
if ($result.Error) { throw $result.Error }
