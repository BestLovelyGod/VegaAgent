# Convert-Data.ps1
# 数据格式转换: JSON/XML/CSV 之间互相转换
# 参数: InputFile(输入文件), InputFormat(输入格式), OutputFormat(输出格式), OutputFile(输出文件)

param(
    [string]$InputFile = "",

    [string]$InputData = "",

    [ValidateSet("JSON", "XML", "CSV", "Auto")]
    [string]$InputFormat = "Auto",

    [ValidateSet("JSON", "XML", "CSV")]
    [string]$OutputFormat = "JSON",

    [string]$OutputFile = ""
)

$result = [ordered]@{
    Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    InputFormat = $InputFormat
    OutputFormat = $OutputFormat
    Output = $null
    Error = $null
}

try {
    # 读取输入数据
    if ($InputFile) {
        if (-not (Test-Path $InputFile)) {
            $result.Error = "输入文件不存在: $InputFile"
            $result | ConvertTo-Json -Depth 3
            return
        }
        $rawData = Get-Content -Path $InputFile -Raw -ErrorAction Stop
    } elseif ($InputData) {
        $rawData = $InputData
    } else {
        $result.Error = "需要提供 InputFile 或 InputData"
        $result | ConvertTo-Json -Depth 3
        return
    }

    # 自动检测输入格式
    if ($InputFormat -eq "Auto") {
        $trimmed = $rawData.TrimStart()
        if ($trimmed.StartsWith("{") -or $trimmed.StartsWith("[")) {
            $InputFormat = "JSON"
        } elseif ($trimmed.StartsWith("<")) {
            $InputFormat = "XML"
        } else {
            $InputFormat = "CSV"
        }
    }

    # 解析输入数据
    $data = switch ($InputFormat) {
        "JSON" {
            $rawData | ConvertFrom-Json
        }
        "XML" {
            [xml]$rawData
        }
        "CSV" {
            $rawData | ConvertFrom-Csv
        }
    }

    # 转换输出格式
    $output = switch ($OutputFormat) {
        "JSON" {
            if ($InputFormat -eq "XML") {
                # XML -> JSON 特殊处理
                $data | ConvertTo-Json -Depth 10
            } else {
                $data | ConvertTo-Json -Depth 10
            }
        }
        "XML" {
            if ($InputFormat -eq "JSON") {
                # JSON -> XML
                $xml = New-Object System.Xml.XmlDocument
                $root = $xml.CreateElement("Root")
                $xml.AppendChild($root) | Out-Null

                foreach ($prop in $data.PSObject.Properties) {
                    $element = $xml.CreateElement($prop.Name)
                    $element.InnerText = $prop.Value
                    $root.AppendChild($element) | Out-Null
                }
                $xml.OuterXml
            } else {
                $data | ConvertTo-Xml -As String
            }
        }
        "CSV" {
            $data | ConvertTo-Csv -NoTypeInformation
        }
    }

    $result.Output = $output

    # 输出到文件
    if ($OutputFile) {
        $output | Out-File -FilePath $OutputFile -Encoding UTF8 -ErrorAction Stop
        $result.Status = "已保存到: $OutputFile"
    }
} catch {
    $result.Error = $_.Exception.Message
}

$result | ConvertTo-Json -Depth 5
