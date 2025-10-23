@{
    Run = @{
        Path = 'Tests/Integration'
        PassThru = $true
    }
    Output = @{
        Verbosity = 'Minimal'
        StackTraceVerbosity = 'None'
        CIFormat = 'None'
    }
    Should = @{
        ErrorAction = 'SilentlyContinue'
    }
    Debug = @{
        ShowFullErrors = $false
    }
}
