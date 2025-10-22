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
        ErrorAction = 'Continue'
    }
    Debug = @{
        ShowFullErrors = $false
    }
}
