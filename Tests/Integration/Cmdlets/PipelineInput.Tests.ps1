# Add-LinesToFile.PipelineInput.Tests.ps1
# Pipeline input tests for Add-LinesToFile and Update-LinesInFile

#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

Describe "Add-LinesToFile Pipeline Input Tests" {
    BeforeEach {
        $script:testFile = [System.IO.Path]::GetTempFileName()
    }

    AfterEach {
        if (Test-Path $script:testFile) {
            Remove-Item $script:testFile -Force -ErrorAction SilentlyContinue
        }
    }

    Context "Content input from the pipeline" {
        It "can create a new file by piping an array" {
            "line1", "line2", "line3" | Add-LinesToFile -Path $script:testFile
            $result = @(Get-Content $script:testFile)
            $result.Count | Should -Be 3
            $result[0] | Should -Be "line1"
            $result[1] | Should -Be "line2"
            $result[2] | Should -Be "line3"
        }

        It "can pipe a single string" {
            "single line" | Add-LinesToFile -Path $script:testFile
            $result = @(Get-Content $script:testFile)
            $result.Count | Should -Be 1
            $result[0] | Should -Be "single line"
        }

        It "pipe input is processed in a single batch (not multiple file accesses)" {
            # Append to an existing file
            Set-Content -Path $script:testFile -Value "existing" -Encoding UTF8
            "new1", "new2" | Add-LinesToFile -Path $script:testFile
            $result = @(Get-Content $script:testFile)
            $result.Count | Should -Be 3
            $result[0] | Should -Be "existing"
            $result[1] | Should -Be "new1"
            $result[2] | Should -Be "new2"
        }

        It "works as before when Content is specified as an argument" {
            Add-LinesToFile -Path $script:testFile -Content "arg1", "arg2"
            $result = @(Get-Content $script:testFile)
            $result.Count | Should -Be 2
            $result[0] | Should -Be "arg1"
            $result[1] | Should -Be "arg2"
        }

        It "can use pipe input combined with LineNumber" {
            Set-Content -Path $script:testFile -Value @("line1", "line3") -Encoding UTF8
            "inserted" | Add-LinesToFile -Path $script:testFile -LineNumber 2
            $result = @(Get-Content $script:testFile)
            $result.Count | Should -Be 3
            $result[0] | Should -Be "line1"
            $result[1] | Should -Be "inserted"
            $result[2] | Should -Be "line3"
        }
    }
}

Describe "Update-LinesInFile Pipeline Input Tests" {
    BeforeEach {
        $script:testFile = [System.IO.Path]::GetTempFileName()
        Set-Content -Path $script:testFile -Value @("line1", "line2", "line3", "line4") -Encoding UTF8
    }

    AfterEach {
        if (Test-Path $script:testFile) {
            Remove-Item $script:testFile -Force -ErrorAction SilentlyContinue
        }
    }

    Context "Content input from the pipeline" {
        It "can replace lines by piping an array" {
            "replaced1", "replaced2" | Update-LinesInFile -Path $script:testFile -LineRange 1,2
            $result = @(Get-Content $script:testFile)
            $result.Count | Should -Be 4
            $result[0] | Should -Be "replaced1"
            $result[1] | Should -Be "replaced2"
            $result[2] | Should -Be "line3"
            $result[3] | Should -Be "line4"
        }

        It "can replace lines by piping a single string" {
            "single" | Update-LinesInFile -Path $script:testFile -LineRange 2,3
            $result = @(Get-Content $script:testFile)
            $result.Count | Should -Be 3
            $result[0] | Should -Be "line1"
            $result[1] | Should -Be "single"
            $result[2] | Should -Be "line4"
        }

        It "pipe input is processed in a single batch" {
            "new1", "new2", "new3" | Update-LinesInFile -Path $script:testFile -LineRange 2,2
            $result = @(Get-Content $script:testFile)
            $result.Count | Should -Be 6
            $result[0] | Should -Be "line1"
            $result[1] | Should -Be "new1"
            $result[2] | Should -Be "new2"
            $result[3] | Should -Be "new3"
            $result[4] | Should -Be "line3"
            $result[5] | Should -Be "line4"
        }

        It "works as before when Content is specified as an argument" {
            Update-LinesInFile -Path $script:testFile -LineRange 1,1 -Content "arg-replaced"
            $result = @(Get-Content $script:testFile)
            $result[0] | Should -Be "arg-replaced"
        }
    }
}