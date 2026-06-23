# Invoke-ApiCall.ps1
# REST API 调用: 支持 GET/POST/PUT/DELETE，自定义头和认证
# 参数: Url(地址), Method(方法), Body(请求体), Headers(请求头), Token(认证令牌)

param(
    [Parameter(Mandatory)]
    [string]$Url,

    [ValidateSet("GET", "POST", "PUT", "DELETE", "PATCH")]
    [string]$Method = "GET",

    [string]$Body = "",

    [string]$Headers = "",

    [string]$Token = "",

    [string]$ContentType = "application/json",

    [int]$TimeoutSec = 30
)

$result = [ordered]@{
    Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Request = [ordered]@{
        Method = $Method
        Url = $Url
    }
    Response = $null
    Error = $null
}

try {
    $params = @{
        Uri = $Url
        Method = $Method
        TimeoutSec = $TimeoutSec
        UseBasicParsing = $true
        ErrorAction = "Stop"
    }

    # 内容类型
    if ($Method -in @("POST", "PUT", "PATCH") -and $Body) {
        $params.ContentType = $ContentType
        $params.Body = $Body
    }

    # 自定义请求头
    $headerHash = @{}
    if ($Headers) {
        try {
            $parsed = $Headers | ConvertFrom-Json
            $parsed.PSObject.Properties | ForEach-Object {
                $headerHash[$_.Name] = $_.Value
            }
        } catch {
            # 尝试解析 "Key: Value" 格式
            $Headers -split "`n" | ForEach-Object {
                $parts = $_ -split ":", 2
                if ($parts.Count -eq 2) {
                    $headerHash[$parts[0].Trim()] = $parts[1].Trim()
                }
            }
        }
    }

    # 认证令牌
    if ($Token) {
        $headerHash["Authorization"] = "Bearer $Token"
    }

    if ($headerHash.Count -gt 0) {
        $params.Headers = $headerHash
    }

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $response = Invoke-RestMethod @params
    $sw.Stop()

    $result.Response = [ordered]@{
        StatusCode = 200
        ElapsedMs = $sw.ElapsedMilliseconds
        Body = $response
    }
} catch {
    $statusCode = if ($_.Exception.Response) { $_.Exception.Response.StatusCode.Value__ } else { 0 }
    $result.Error = $_.Exception.Message
    $result.Response = [ordered]@{
        StatusCode = $statusCode
        Error = $_.Exception.Message
    }
}

$result | ConvertTo-Json -Depth 5
