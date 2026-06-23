@{
    RootModule        = 'Agent.CLI.psm1'
    ModuleVersion     = '1.0.0'
    GUID              = 'a1b2c3d4-e5f6-7890-abcd-ef1234567890'
    Author            = 'Ignorant Vega'
    CompanyName       = 'Personal'
    Copyright         = '(c) 2026 Ignorant Vega. All rights reserved.'
    Description       = 'Ignorant Vega CLI �?提交任务、查看状态、管理工�?
    PowerShellVersion = '7.0'
    FunctionsToExport = @(
        'Invoke-AgentTask'
        'Get-AgentStatus'
        'Get-AgentTools'
        'Watch-AgentTask'
    )
    CmdletsToExport   = @()
    VariablesToExport  = @()
    AliasesToExport    = @()
    PrivateData = @{
        PSData = @{
            Tags       = @('Agent', 'AI', 'CLI', 'Automation')
            ProjectUri = 'https://github.com/ignorant-agent'
        }
    }
}
