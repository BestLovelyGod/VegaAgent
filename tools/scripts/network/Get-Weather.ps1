# Get-Weather.ps1
# 天气查询: 通过 wttr.in 获取实时天气数据
# 参数: City(城市名, 默认自动检测), Format(输出格式 json/text)

param(
    [string]$City = "",
    [ValidateSet("json", "text")]
    [string]$Format = "json"
)

$result = [ordered]@{
    Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    City      = if ($City) { $City } else { "自动检测" }
    Weather   = $null
    Error     = $null
}

try {
    $cityParam = if ($City) { [System.Uri]::EscapeDataString($City) } else { "" }
    $url = "https://wttr.in/$cityParam`?format=j1"

    # 重试机制: 最多 3 次, 超时 30 秒
    $response = $null
    for ($retry = 0; $retry -lt 3; $retry++) {
        try {
            $response = Invoke-RestMethod -Uri $url -TimeoutSec 30 -ErrorAction Stop
            break
        } catch {
            if ($retry -lt 2) { Start-Sleep -Seconds 2 } else { throw }
        }
    }

    $current = $response.current_condition[0]
    $area = $response.nearest_area[0]

    $weather = [ordered]@{
        Location  = [ordered]@{
            City       = $area.areaName[0].value
            Region     = $area.region[0].value
            Country    = $area.country[0].value
        }
        Current   = [ordered]@{
            Temperature  = [int]$current.temp_C
            FeelsLike    = [int]$current.FeelsLikeC
            Humidity     = [int]$current.humidity
            WindSpeed    = [int]$current.windspeedKmph
            WindDir      = $current.winddir16Point
            Visibility   = [int]$current.visibility
            CloudCover   = [int]$current.cloudcover
            UVIndex      = [int]$current.uvIndex
            Description  = $current.lang_zh[0].value
        }
        Today     = [ordered]@{
            MaxTemp = [int]$response.weather[0].maxtempC
            MinTemp = [int]$response.weather[0].mintempC
            Sunrise = $response.weather[0].astronomy[0].sunrise
            Sunset  = $response.weather[0].astronomy[0].sunset
        }
    }

    if ($Format -eq "text") {
        $text = "$($weather.Location.City) ($($weather.Location.Region), $($weather.Location.Country))`n"
        $text += "当前: $($weather.Current.Description) $($weather.Current.Temperature)°C (体感 $($weather.Current.FeelsLike)°C)`n"
        $text += "湿度: $($weather.Current.Humidity)% | 风: $($weather.Current.WindDir) $($weather.Current.WindSpeed)km/h`n"
        $text += "今日: $($weather.Today.MinTemp)°C ~ $($weather.Today.MaxTemp)°C | 日出 $($weather.Today.Sunrise) 日落 $($weather.Today.Sunset)"
        Write-Output $text
    } else {
        $result.Weather = $weather
        $result | ConvertTo-Json -Depth 5
    }
} catch {
    $result.Error = $_.Exception.Message
    $result | ConvertTo-Json -Depth 3
}
