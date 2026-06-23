# Manage-ACL.ps1
# 权限管理: 查看/修改文件和目录的 ACL (访问控制列表)
# 参数: Path(路径), Action(操作), Identity(用户/组), Permission(权限), Inheritance(继承)

param(
    [Parameter(Mandatory)]
    [string]$Path,

    [ValidateSet("Get", "Grant", "Revoke", "Reset")]
    [string]$Action = "Get",

    [string]$Identity = "",

    [ValidateSet("FullControl", "Modify", "ReadAndExecute", "Read", "Write")]
    [string]$Permission = "ReadAndExecute",

    [ValidateSet("Allow", "Deny")]
    [string]$AccessType = "Allow",

    [switch]$Inherit
)

$result = [ordered]@{
    Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Action = $Action
    Path = $Path
    Owner = $null
    AccessRules = @()
    Error = $null
    Status = $null
}

try {
    if (-not (Test-Path $Path)) {
        $result.Error = "路径不存在: $Path"
        $result | ConvertTo-Json -Depth 5
        return
    }

    $acl = Get-Acl -Path $Path -ErrorAction Stop
    $result.Owner = $acl.Owner

    switch ($Action) {
        "Get" {
            $result.AccessRules = $acl.Access | ForEach-Object {
                [ordered]@{
                    Identity = $_.IdentityReference.ToString()
                    Rights = $_.FileSystemRights.ToString()
                    AccessType = $_.AccessControlType.ToString()
                    Inherited = $_.IsInherited
                    InheritanceFlags = $_.InheritanceFlags.ToString()
                    PropagationFlags = $_.PropagationFlags.ToString()
                }
            }
        }
        "Grant" {
            if (-not $Identity) { throw "需要指定用户/组 (Identity)" }

            $rights = [System.Security.AccessControl.FileSystemRights]::$Permission
            $inheritFlags = if ($Inherit) {
                [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
                [System.Security.AccessControl.InheritanceFlags]::ObjectInherit
            } else {
                [System.Security.AccessControl.InheritanceFlags]::None
            }

            $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
                $Identity, $rights, $inheritFlags,
                [System.Security.AccessControl.PropagationFlags]::None,
                [System.Security.AccessControl.AccessControlType]::$AccessType
            )

            $acl.AddAccessRule($rule)
            Set-Acl -Path $Path -AclObject $acl -ErrorAction Stop
            $result.Status = "已授予 $Identity $Permission 权限"
        }
        "Revoke" {
            if (-not $Identity) { throw "需要指定用户/组 (Identity)" }

            $rulesToRemove = $acl.Access | Where-Object {
                $_.IdentityReference.ToString() -like "*$Identity*"
            }

            foreach ($rule in $rulesToRemove) {
                $acl.RemoveAccessRule($rule) | Out-Null
            }

            Set-Acl -Path $Path -AclObject $acl -ErrorAction Stop
            $result.Status = "已撤销 $Identity 的权限"
        }
        "Reset" {
            $acl.SetAccessRuleProtection($true, $false)
            $owner = New-Object System.Security.Principal.NTAccount($env:USERNAME)
            $acl.SetOwner($owner)

            $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
                $env:USERNAME, "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow"
            )
            $acl.AddAccessRule($rule)

            Set-Acl -Path $Path -AclObject $acl -ErrorAction Stop
            $result.Status = "ACL 已重置"
        }
    }
} catch {
    $result.Error = $_.Exception.Message
}

$result | ConvertTo-Json -Depth 5
