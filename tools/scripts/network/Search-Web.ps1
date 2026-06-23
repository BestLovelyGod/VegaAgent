# Search-Web.ps1
# 联网搜索工具: 支持必应和百度搜索引擎，可限定特定网站
# 参数: Query(搜索查询), Engine(搜索引擎), Site(限定网站), MaxResults(最大结果数)

param(
    [Parameter(Mandatory)]
    [string]$Query,

    [string]$Engine = "bing",

    [string]$Site = "",

    [int]$MaxResults = 5,

    [string]$Language = "zh-CN"
)

# 不支持的引擎自动回退到 bing
$Engine = $Engine.ToLowerInvariant()
if ($Engine -notin @("bing", "baidu")) {
    Write-Warning "不支持的搜索引擎: $Engine，自动回退到 bing"
    $Engine = "bing"
}

$result = [ordered]@{
    Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Query = $Query
    Engine = $Engine
    Site = $Site
    Results = @()
    Error = $null
}

try {
    # 构建搜索URL
    $searchQuery = $Query
    if ($Site) {
        $searchQuery += " site:$Site"
    }

    $encodedQuery = [System.Uri]::EscapeDataString($searchQuery)

    $url = switch ($Engine) {
        "bing" { "https://cn.bing.com/search?q=$encodedQuery&setlang=$Language" }
        "baidu" { "https://www.baidu.com/s?wd=$encodedQuery&rn=$MaxResults" }
    }

    # 设置请求头，模拟浏览器
    $headers = @{
        "User-Agent" = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
        "Accept" = "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8"
        "Accept-Language" = "$Language,en-US;q=0.7,en;q=0.3"
        "Accept-Encoding" = "gzip, deflate"
    }

    # 发送请求
    $response = Invoke-WebRequest -Uri $url -Headers $headers -UseBasicParsing -TimeoutSec 30 -ErrorAction Stop

    # 解析搜索结果
    $searchResults = @()

    if ($Engine -eq "bing") {
        # 解析必应搜索结果
        $html = $response.Content
        
        # 匹配搜索结果项 (li.b_algo)
        $resultPattern = '<li class="b_algo">([\s\S]*?)</li>'
        $matches = [regex]::Matches($html, $resultPattern)
        
        foreach ($match in $matches) {
            if ($searchResults.Count -ge $MaxResults) { break }
            
            $itemHtml = $match.Groups[1].Value
            
            # 提取标题和链接
            $titlePattern = '<a[^>]*href="([^"]*)"[^>]*>([\s\S]*?)</a>'
            $titleMatch = [regex]::Match($itemHtml, $titlePattern)
            
            if ($titleMatch.Success) {
                $link = $titleMatch.Groups[1].Value
                $title = $titleMatch.Groups[2].Value -replace '<[^>]*>', '' -replace '&amp;', '&' -replace '&lt;', '<' -replace '&gt;', '>' -replace '&quot;', '"'
                $title = $title.Trim()
                
                # 提取摘要
                $snippetPattern = '<p[^>]*>([\s\S]*?)</p>'
                $snippetMatch = [regex]::Match($itemHtml, $snippetPattern)
                $snippet = ""
                if ($snippetMatch.Success) {
                    $snippet = $snippetMatch.Groups[1].Value -replace '<[^>]*>', '' -replace '&amp;', '&' -replace '&lt;', '<' -replace '&gt;', '>' -replace '&quot;', '"'
                    $snippet = $snippet.Trim()
                }
                
                if ($title -and $link) {
                    $searchResults += [ordered]@{
                        Title = $title
                        Link = $link
                        Snippet = $snippet
                    }
                }
            }
        }
    }
    elseif ($Engine -eq "baidu") {
        # 解析百度搜索结果
        $html = $response.Content
        
        # 匹配搜索结果项 (div.result)
        $resultPattern = '<div class="result[^"]*"[^>]*>([\s\S]*?)</div>\s*<div'
        $matches = [regex]::Matches($html, $resultPattern)
        
        foreach ($match in $matches) {
            if ($searchResults.Count -ge $MaxResults) { break }
            
            $itemHtml = $match.Groups[1].Value
            
            # 提取标题和链接
            $titlePattern = '<a[^>]*href="([^"]*)"[^>]*>([\s\S]*?)</a>'
            $titleMatch = [regex]::Match($itemHtml, $titlePattern)
            
            if ($titleMatch.Success) {
                $link = $titleMatch.Groups[1].Value
                $title = $titleMatch.Groups[2].Value -replace '<[^>]*>', '' -replace '&amp;', '&' -replace '&lt;', '<' -replace '&gt;', '>' -replace '&quot;', '"'
                $title = $title.Trim()
                
                # 提取摘要
                $snippetPattern = '<span class="content-right_[^"]*">([\s\S]*?)</span>'
                $snippetMatch = [regex]::Match($itemHtml, $snippetPattern)
                $snippet = ""
                if ($snippetMatch.Success) {
                    $snippet = $snippetMatch.Groups[1].Value -replace '<[^>]*>', '' -replace '&amp;', '&' -replace '&lt;', '<' -replace '&gt;', '>' -replace '&quot;', '"'
                    $snippet = $snippet.Trim()
                }
                
                if ($title -and $link) {
                    $searchResults += [ordered]@{
                        Title = $title
                        Link = $link
                        Snippet = $snippet
                    }
                }
            }
        }
    }

    $result.Results = $searchResults
    $result | ConvertTo-Json -Depth 5

}
catch {
    $result.Error = "搜索失败: $($_.Exception.Message)"
    $result | ConvertTo-Json -Depth 5
}