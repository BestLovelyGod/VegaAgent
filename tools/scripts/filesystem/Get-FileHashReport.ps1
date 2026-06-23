# Get-FileHashReport.ps1
# 文件哈希校验: 批量计算文件哈希值，支持校验验证
# 参数: Path(文件/目录), Algorithm(算法), Pattern(文件模式), VerifyHash(校验哈希)

param(
    [Parameter(Mandatory)]
    [string]$Path,

    [ValidateSet("MD5", "SHA1", "SHA256", "SHA512")]
    [string]$Algorithm = "SHA256",

    [string]$Pattern = "*",

    [string]$VerifyHash = ""
)

$result = [ordered]@{
    Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Path = $Path
    Algorithm = $Algorithm
    TotalFiles = 0
    Files = @()
    Verification = $null
    Error = $null
}

try {
    if (-not (Test-Path $Path)) {
        $result.Error = "路径不存在: $Path"
        $result | ConvertTo-Json -Depth 5
        return
    }

    $item = Get-Item $Path

    if ($item.PSIsContainer) {
        # 目录模式: 计算所有匹配文件的哈希
        $files = Get-ChildItem -Path $Path -Filter $Pattern -File -Recurse -ErrorAction SilentlyContinue
    } else {
        # 单文件模式
        $files = @($item)
    }

    $result.TotalFiles = ($files | Measure-Object).Count

    $result.Files = $files | ForEach-Object {
        $hash = (Get-FileHash -Path $_.FullName -Algorithm $Algorithm -ErrorAction SilentlyContinue).Hash

        [ordered]@{
            Name = $_.Name
            Path = $_.FullName
            SizeMB = [math]::Round($_.Length / 1MB, 2)
            Hash = $hash
            LastModified = $_.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
        }
    }

    # 哈希校验模式
    if ($VerifyHash -and $result.TotalFiles -eq 1) {
        $actualHash = $result.Files[0].Hash
        $result.Verification = [ordered]@{
            Expected = $VerifyHash.ToUpper()
            Actual = $actualHash
            Match = $actualHash -eq $VerifyHash.ToUpper()
        }
    }
} catch {
    $result.Error = $_.Exception.Message
}

$result | ConvertTo-Json -Depth 5
