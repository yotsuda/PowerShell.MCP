# Update-LinesInFile.Tests.ps1
# Integration tests for the Update-LinesInFile cmdlet

#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

Describe "Update-LinesInFile Integration Tests" {
    BeforeEach {
        # Create a new temp file before each test
        $script:testFile = [System.IO.Path]::GetTempFileName()
        $script:initialContent = @(
            "Line 1: First line"
            "Line 2: Second line"
            "Line 3: Third line"
            "Line 4: Fourth line"
            "Line 5: Fifth line"
        )
        Set-Content -Path $script:testFile -Value $script:initialContent -Encoding UTF8
    }

    AfterEach {
        # Clean up after each test
        if (Test-Path $script:testFile) {
            Remove-Item $script:testFile -Force
        }
        # Clean up backup files too
        Get-ChildItem -Path (Split-Path $script:testFile) -Filter "$([System.IO.Path]::GetFileName($script:testFile))*" | 
            Where-Object { $_.FullName -ne $script:testFile } | Remove-Item -Force
    }

    Context "Updating a single line" {
        It "can replace one line with new content" {
            Update-LinesInFile -Path $script:testFile -LineRange 2 -Content "Updated Line 2"
            $result = Get-Content $script:testFile
            $result[1] | Should -Be "Updated Line 2"
            $result.Count | Should -Be 5
        }

        It "can update the first line" {
            Update-LinesInFile -Path $script:testFile -LineRange 1 -Content "New First Line"
            $result = Get-Content $script:testFile
            $result[0] | Should -Be "New First Line"
        }

        It "can update the last line" {
            Update-LinesInFile -Path $script:testFile -LineRange 5 -Content "New Last Line"
            $result = Get-Content $script:testFile
            $result[-1] | Should -Be "New Last Line"
        }
    }

    Context "Updating multiple lines" {
        It "can replace multiple consecutive lines" {
            $newContent = @("New Line 2", "New Line 3")
            Update-LinesInFile -Path $script:testFile -LineRange 2,3 -Content $newContent
            $result = Get-Content $script:testFile
            $result[1] | Should -Be "New Line 2"
            $result[2] | Should -Be "New Line 3"
            $result.Count | Should -Be 5
        }

        It "can replace multiple lines with one line (line count decreases)" {
            Update-LinesInFile -Path $script:testFile -LineRange 2,4 -Content "Single Replacement"
            $result = Get-Content $script:testFile
            $result.Count | Should -Be 3
            $result[1] | Should -Be "Single Replacement"
        }

        It "can replace one line with multiple lines (line count increases)" {
            $newContent = @("Expanded Line A", "Expanded Line B", "Expanded Line C")
            Update-LinesInFile -Path $script:testFile -LineRange 3 -Content $newContent
            $result = Get-Content $script:testFile
            $result.Count | Should -Be 7
            $result[2..4] | Should -Be $newContent
        }
    }

    Context "Deleting lines" {
        It "errors when Content is omitted" {
            { Update-LinesInFile -Path $script:testFile -LineRange 3 } | Should -Throw "*Content is required*"
        }

        It "can delete a single line with -Content @()" {
            Update-LinesInFile -Path $script:testFile -LineRange 3,3 -Content @()
            $result = Get-Content $script:testFile
            $result.Count | Should -Be 4
            $result -notcontains "Line 3: Third line" | Should -Be $true
        }

        It "can delete multiple lines with -Content @()" {
            Update-LinesInFile -Path $script:testFile -LineRange 2,4 -Content @()
            $result = Get-Content $script:testFile
            $result.Count | Should -Be 2
            $result[0] | Should -Be "Line 1: First line"
            $result[1] | Should -Be "Line 5: Fifth line"
        }
    }

    Context "Encoding" {
        It "can update a UTF-8 file correctly" {
            $content = "日本語テキスト 🎌"
            Update-LinesInFile -Path $script:testFile -LineRange 1 -Content $content -Encoding UTF8
            $result = Get-Content $script:testFile -Encoding UTF8
            $result[0] | Should -Be $content
        }

        It "updating an ASCII file with Japanese automatically upgrades it to UTF-8" {
            # Create a file with ASCII encoding
            $asciiFile = [System.IO.Path]::GetTempFileName()
            [System.IO.File]::WriteAllLines($asciiFile, @("Line 1", "Line 2", "Line 3"), [System.Text.Encoding]::ASCII)
            
            try {
                # Confirm the file encoding is ASCII
                $bytes = [System.IO.File]::ReadAllBytes($asciiFile)
                $encoding = [System.Text.Encoding]::ASCII
                $detectedText = $encoding.GetString($bytes)
                $detectedText | Should -Not -BeNullOrEmpty
                
                # Update the line with content containing Japanese (without specifying the Encoding parameter)
                $infoMessages = @()
                Update-LinesInFile -Path $asciiFile -LineRange 2 -Content "日本語の更新テスト" -InformationVariable infoMessages
                
                # Confirm the encoding-upgrade information message is emitted
                $infoMessages | Should -Not -BeNullOrEmpty
                $infoMessages.MessageData -join ' ' | Should -Match 'UTF-8'
                
                # Confirm the file can be read as UTF-8
                $result = Get-Content $asciiFile -Encoding UTF8
                $result[1] | Should -Be "日本語の更新テスト"

                # Confirm it is correctly saved as UTF-8
                $content = [System.IO.File]::ReadAllText($asciiFile, [System.Text.Encoding]::UTF8)
                $content | Should -Match "日本語の更新テスト"
            }
            finally {
                if (Test-Path $asciiFile) {
                    Remove-Item $asciiFile -Force
                }
            }
        }
    }

    Context "Backup feature" {
        It "creates a backup file when -Backup is specified" {
            Update-LinesInFile -Path $script:testFile -LineRange 1 -Content "Updated" -Backup
            $backupFiles = Get-ChildItem -Path (Split-Path $script:testFile) -Filter "$([System.IO.Path]::GetFileName($script:testFile))*.bak"
            $backupFiles.Count | Should -BeGreaterThan 0
        }

        It "saves the original content in the backup file" {
            $originalContent = Get-Content $script:testFile
            Update-LinesInFile -Path $script:testFile -LineRange 1 -Content "Updated" -Backup
            $backupFile = Get-ChildItem -Path (Split-Path $script:testFile) -Filter "$([System.IO.Path]::GetFileName($script:testFile))*.bak" | Select-Object -First 1
            $backupContent = Get-Content $backupFile.FullName
            $backupContent | Should -Be $originalContent
        }
    }

    Context "WhatIf and Confirm" {
        It "does not actually change the file when -WhatIf is specified" {
            $originalContent = Get-Content $script:testFile
            Update-LinesInFile -Path $script:testFile -LineRange 1 -Content "Updated" -WhatIf
            $result = Get-Content $script:testFile
            $result | Should -Be $originalContent
        }
    }

    Context "Error handling" {
        It "errors on a nonexistent file" {
            { Update-LinesInFile -Path "C:\NonExistent\file.txt" -LineRange 1 -Content "Test" -ErrorAction Stop } | 
                Should -Throw
        }

        It "errors on an out-of-range line number" {
            { Update-LinesInFile -Path $script:testFile -LineRange 100 -Content "Test" -ErrorAction Stop } | 
                Should -Throw
        }

        It "errors on an invalid range specification" {
            { Update-LinesInFile -Path $script:testFile -LineRange 5,2 -Content "Test" -ErrorAction Stop } | 
                Should -Throw
        }
    }

    Context "Pipeline input" {
        It "can process multiple files from the pipeline" {
            $file2 = [System.IO.Path]::GetTempFileName()
            Set-Content -Path $file2 -Value @("File2 Line1", "File2 Line2")
            
            try {
                Get-Item @($script:testFile, $file2) | Update-LinesInFile -LineRange 1 -Content "Updated"
                (Get-Content $script:testFile)[0] | Should -Be "Updated"
                (Get-Content $file2)[0] | Should -Be "Updated"
            }
            finally {
                Remove-Item $file2 -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context "Message display" {
        It "displays the correct line count when replacing a line range with -LineRange 1,-1" {
            $output = Update-LinesInFile -Path $script:testFile -LineRange 1,-1 -Content "A","B","C"

            # Confirm the message is in the "Replaced X line(s)" format and shows the correct line count
            $message = $output | Out-String
            $message | Should -Match "Replaced \d+ line\(s\) with \d+ line\(s\)"
            $message | Should -Not -Match "\d{4,}"  # Must not contain a number with 4 or more digits (e.g. int.MaxValue)
        }

        It "displays the conventional message for a normal LineRange" {
            $output = Update-LinesInFile -Path $script:testFile -LineRange 2,4 -Content "X","Y","Z"

            # Confirm the message is in the "Replaced X line(s)" format
            $message = $output | Out-String
            $message | Should -Match "Replaced \d+ line\(s\)"
        }

        It "displays a Removed message on deletion" {
            $output = Update-LinesInFile -Path $script:testFile -LineRange 2,4 -Content @()
            
            $message = $output | Out-String
            $message | Should -Match "Removed \d+ line\(s\)"
        }

    Context "Parameter Aliases" {
        It "-NewLines alias works for -Content with single line" {
            Update-LinesInFile -Path $script:testFile -LineRange 2 -NewLines "Replaced via alias"
            $result = Get-Content $script:testFile
            $result[1] | Should -Be "Replaced via alias"
            $result.Count | Should -Be 5
        }

        It "-NewLines alias works for -Content with multiple lines" {
            Update-LinesInFile -Path $script:testFile -LineRange 2,3 -NewLines @("New A", "New B", "New C")
            $result = Get-Content $script:testFile
            $result[1] | Should -Be "New A"
            $result[2] | Should -Be "New B"
            $result[3] | Should -Be "New C"
            $result.Count | Should -Be 6
        }

        It "-NewLines alias works for deletion with empty array" {
            Update-LinesInFile -Path $script:testFile -LineRange 3 -NewLines @()
            $result = Get-Content $script:testFile
            $result.Count | Should -Be 4
            $result[2] | Should -Be "Line 4: Fourth line"
        }
    }
    }
}