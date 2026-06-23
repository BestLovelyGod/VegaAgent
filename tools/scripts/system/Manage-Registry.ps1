# Manage-Registry.ps1
# 注册表操作: 查询/创建/修改/删除注册表项和值
# 参数: Path(注册表路径), Action(操作), Name(值名), Value(值数据), Type(值类型)

param(
    [string]$Path = "HKLM:\SOFTWARE",

    [ValidateSet("Get", "SetValue", "DeleteValue", "CreateKey", "DeleteKey")]
    [string]$Action = "Get",

    [string]$Name = "",

    [string]$Value = "",

    [ValidateSet("String", "ExpandString", "Binary", "DWord", "MultiString", "QWord")]
    [string]$Type = "String"
)

$result = [ordered]@{
    Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Action = $Action
    Path = $Path
    Error = $null
    Data = $null
}

try {
    switch ($Action) {
        "Get" {
            if (-not (Test-Path $Path)) {
                $result.Error = "路径不存在: $Path"
                break
            }

            if ($Name) {
                # 获取指定值
                $val = Get-ItemProperty -Path $Path -Name $Name -ErrorAction Stop
                $result.Data = [ordered]@{
                    Name = $Name
                    Value = $val.$Name
                    Type = (Get-Item $Path).GetValueKind($Name).ToString()
                }
            } else {
                # 列出所有值
                $item = Get-Item $Path -ErrorAction Stop
                $values = @()
                foreach ($valName in $item.GetValueNames()) {
                    if ($valName) {
                        $values += [ordered]@{
                            Name = $valName
                            Value = $item.GetValue($valName)
                            Type = $item.GetValueKind($valName).ToString()
                        }
                    }
                }
                $result.Data = [ordered]@{
                    KeyPath = $Path
                    SubKeyCount = ($item.GetSubKeyNames() | Measure-Object).Count
                    ValueCount = ($values | Measure-Object).Count
                    Values = $values
                    SubKeys = @($item.GetSubKeyNames() | Select-Object -First 50)
                }
            }
        }
        "SetValue" {
            if (-not $Name) { throw "需要指定值名称 (Name)" }
            if (-not (Test-Path $Path)) {
                New-Item -Path $Path -Force -ErrorAction Stop | Out-Null
            }
            $typeParam = if ($Type -eq "DWord") { "DWord" } else { $Type }
            Set-ItemProperty -Path $Path -Name $Name -Value $Value -Type $typeParam -ErrorAction Stop
            $result.Data = [ordered]@{ Name = $Name; Value = $Value; Type = $Type; Status = "已设置" }
        }
        "DeleteValue" {
            if (-not $Name) { throw "需要指定值名称 (Name)" }
            Remove-ItemProperty -Path $Path -Name $Name -ErrorAction Stop
            $result.Data = [ordered]@{ Name = $Name; Status = "已删除" }
        }
        "CreateKey" {
            New-Item -Path $Path -Force -ErrorAction Stop | Out-Null
            $result.Data = [ordered]@{ Path = $Path; Status = "已创建" }
        }
        "DeleteKey" {
            Remove-Item -Path $Path -Recurse -Force -ErrorAction Stop
            $result.Data = [ordered]@{ Path = $Path; Status = "已删除" }
        }
    }
} catch {
    $result.Error = $_.Exception.Message
}

$result | ConvertTo-Json -Depth 5
