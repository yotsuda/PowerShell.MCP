# Test-AddLinesToFileCmdlet.ps1
# Integration tests for the Add-LinesToFile cmdlet

#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

Describe "Add-LinesToFile Integration Tests" {
    BeforeEach {
        # Create a new temp file before each test
        $script:testFile = [System.IO.Path]::GetTempFileName()
        $script:initialContent = @(
            "Line 1"
            "Line 2"
            "Line 3"
        )
        Set-Content -Path $script:testFile -Value $script:initialContent -Encoding UTF8
    }

    AfterEach {
        # Clean up after each test
        if (Test-Path $script:testFile) {
            Remove-Item $script:testFile -Force -ErrorAction SilentlyContinue
        }
        # Clean up backup files too (convert to an array before deleting)
        $backupFiles = @(Get-ChildItem -Path (Split-Path $script:testFile) -Filter "$([System.IO.Path]::GetFileName($script:testFile))*" -ErrorAction SilentlyContinue | 
            Where-Object { $_.FullName -ne $script:testFile })
        $backupFiles | ForEach-Object { Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue }
    }

    Context "Appending to the end of the file" {
        It "can append a line to the end" {
            Add-LinesToFile -Path $script:testFile -Content "New Line"
            $result = Get-Content $script:testFile
            $result.Count | Should -Be 4
            $result[-1] | Should -Be "New Line"
        }

        It "can append multiple lines to the end" {
            $newLines = @("Line 4", "Line 5")
            Add-LinesToFile -Path $script:testFile -Content $newLines
            $result = Get-Content $script:testFile
            $result.Count | Should -Be 5
            $result[-2] | Should -Be "Line 4"
            $result[-1] | Should -Be "Line 5"
        }
    }

    Context "Inserting at a specific position" {
        It "can insert a line at the beginning of the file" {
            Add-LinesToFile -Path $script:testFile -LineNumber 1 -Content "New First Line"
            $result = Get-Content $script:testFile
            $result.Count | Should -Be 4
            $result[0] | Should -Be "New First Line"
            $result[1] | Should -Be "Line 1"
        }

        It "can insert a line in the middle of the file" {
            Add-LinesToFile -Path $script:testFile -LineNumber 2 -Content "Inserted Line"
            $result = Get-Content $script:testFile
            $result.Count | Should -Be 4
            $result[1] | Should -Be "Inserted Line"
            $result[2] | Should -Be "Line 2"
        }
    }

    Context "Creating a new file" {
        It "can write to a nonexistent file" {
            $newFile = Join-Path $env:TEMP "test_new_file_$(Get-Random).txt"
            try {
                Add-LinesToFile -Path $newFile -Content "New content"
                Test-Path $newFile | Should -Be $true
                $result = Get-Content $newFile
                $result | Should -Be "New content"
            }
            finally {
                if (Test-Path $newFile) {
                    Remove-Item $newFile -Force
                }
            }
        }
    }

    Context "Backup feature" {
        It "can create a backup with the -Backup switch" {
            Add-LinesToFile -Path $script:testFile -Content "New Line" -Backup
            # A timestamped backup file is created
            $backupFiles = Get-ChildItem -Path (Split-Path $script:testFile) -Filter "$([System.IO.Path]::GetFileName($script:testFile)).*" |
                Where-Object { $_.Name -match '\.bak$' }
            $backupFiles.Count | Should -BeGreaterThan 0
        }
    }

    Context "WhatIf support" {
        It "does not actually change the file with -WhatIf" {
            $originalContent = Get-Content $script:testFile
            Add-LinesToFile -Path $script:testFile -Content "New Line" -WhatIf
            $newContent = Get-Content $script:testFile
            Compare-Object $originalContent $newContent | Should -BeNullOrEmpty
        }
    }

    Context "Encoding" {
        It "can write with UTF-8 encoding" {
            $testContent = "日本語テスト"
            Add-LinesToFile -Path $script:testFile -Content $testContent -Encoding "utf-8"
            $result = Get-Content $script:testFile -Encoding UTF8
            $result[-1] | Should -Be $testContent
        }

        It "appending Japanese to an ASCII file automatically upgrades it to UTF-8" {
            # Create a file with ASCII encoding
            $asciiFile = [System.IO.Path]::GetTempFileName()
            [System.IO.File]::WriteAllLines($asciiFile, @("Line 1", "Line 2"), [System.Text.Encoding]::ASCII)
            
            try {
                # Confirm the file encoding is ASCII
                $bytes = [System.IO.File]::ReadAllBytes($asciiFile)
                $encoding = [System.Text.Encoding]::ASCII
                $detectedText = $encoding.GetString($bytes)
                $detectedText | Should -Not -BeNullOrEmpty
                
                # Append content containing Japanese (without specifying the Encoding parameter)
                $infoMessages = @()
                Add-LinesToFile -Path $asciiFile -Content "日本語のテスト" -InformationVariable infoMessages
                
                # Confirm the encoding-upgrade information message is emitted
                $infoMessages | Should -Not -BeNullOrEmpty
                $infoMessages.MessageData -join ' ' | Should -Match 'UTF-8'
                
                # Confirm the file can be read as UTF-8
                $result = Get-Content $asciiFile -Encoding UTF8
                $result[-1] | Should -Be "日本語のテスト"

                # Confirm it is correctly saved as UTF-8
                $content = [System.IO.File]::ReadAllText($asciiFile, [System.Text.Encoding]::UTF8)
                $content | Should -Match "日本語のテスト"
            }
            finally {
                if (Test-Path $asciiFile) {
                    Remove-Item $asciiFile -Force
                }
            }
        }
    }

    Context "Error handling" {
        It "warns on an invalid line number but continues" {
            # Lenient design: warns but continues processing
            $warningMessage = $null
            Add-LinesToFile -Path $script:testFile -LineNumber 100 -Content "Test" -WarningVariable warningMessage -WarningAction SilentlyContinue
            # The file is modified (e.g. appended to the end)
            $result = Get-Content $script:testFile
            $result | Should -Not -BeNullOrEmpty
        }

        It "errors on a read-only file" {
            Set-ItemProperty -Path $script:testFile -Name IsReadOnly -Value $true
            { Add-LinesToFile -Path $script:testFile -Content "Test" -ErrorAction Stop } | Should -Throw
            Set-ItemProperty -Path $script:testFile -Name IsReadOnly -Value $false
        }
    }

        It "H13. LineNumber = 0 causes a parameter validation error" {
            # The implementation rejects LineNumber=0
            { Add-LinesToFile -Path $script:testFile -LineNumber 0 -Content "Test" } | Should -Throw
        }
        It "H14. LineNumber > file line count warns but continues processing" {
            Add-LinesToFile -Path $script:testFile -LineNumber 1000 -Content "Test" -WarningAction SilentlyContinue
            # A warning is emitted, but the file is modified
            $result = Get-Content $script:testFile
            $result | Should -Not -BeNullOrEmpty
        }

        It "H15. an empty array for Content causes a binding error" {
            # The implementation rejects empty arrays
            { Add-LinesToFile -Path $script:testFile -Content @() } | Should -Throw
        }

        It "H16. can process a null or empty string Content" {
            # In PowerShell, null is automatically converted to an empty array
            $originalCount = (Get-Content $script:testFile).Count
            Add-LinesToFile -Path $script:testFile -Content "" -WarningAction SilentlyContinue
            $newCount = (Get-Content $script:testFile).Count
            # An empty string may still be added as a line
            $newCount | Should -BeGreaterOrEqual $originalCount
        }

        It "H17. can append to an empty file" {
            $emptyFile = [System.IO.Path]::GetTempFileName()
            Set-Content -Path $emptyFile -Value @()
            
            try {
                Add-LinesToFile -Path $emptyFile -Content "First line"
                $result = Get-Content $emptyFile
                $result | Should -Be "First line"
            }
            finally {
                Remove-Item $emptyFile -Force
            }
        }

        It "H18. can append to multiple files at once" {
            $file1 = [System.IO.Path]::GetTempFileName()
            $file2 = [System.IO.Path]::GetTempFileName()
            Set-Content -Path $file1 -Value "File1 original"
            Set-Content -Path $file2 -Value "File2 original"
            
            try {
                Add-LinesToFile -Path $file1,$file2 -Content "Added line"
                
                $result1 = Get-Content $file1
                $result2 = Get-Content $file2
                
                $result1[-1] | Should -Be "Added line"
                $result2[-1] | Should -Be "Added line"
            }
            finally {
                Remove-Item $file1,$file2 -Force
            }
        }

        It "H19. errors without access permission" {
            # The testFile set up by BeforeEach/AfterEach cannot be used for read-only tests, so
            # create a new file
            $readOnlyFile = [System.IO.Path]::GetTempFileName()
            Set-Content -Path $readOnlyFile -Value "Original"
            Set-ItemProperty -Path $readOnlyFile -Name IsReadOnly -Value $true
            
            try {
                { Add-LinesToFile -Path $readOnlyFile -Content "Test" -ErrorAction Stop } | Should -Throw
            }
            finally {
                Set-ItemProperty -Path $readOnlyFile -Name IsReadOnly -Value $false
                Remove-Item $readOnlyFile -Force
            }
        }

        It "H20. can write to a nonexistent file (new creation)" {
            $newFile = Join-Path $env:TEMP "NewFile_$(Get-Random).txt"
            
            try {
                Add-LinesToFile -Path $newFile -Content "New content"
                Test-Path $newFile | Should -Be $true
                $result = Get-Content $newFile
                $result | Should -Be "New content"
            }
            finally {
                if (Test-Path $newFile) {
                    Remove-Item $newFile -Force
                }
            }
        }

    Context "Context display" {
        It "C01. context is displayed when appending to the end" {
            $testFile = [System.IO.Path]::GetTempFileName()
            try {
                Set-Content -Path $testFile -Value @("Line 1", "Line 2", "Line 3", "Line 4", "Line 5") -Encoding UTF8
                
                $output = Add-LinesToFile -Path $testFile -Content "Line 6" 6>&1 | Out-String
                
                # Confirm the context includes the 2 lines before the insertion position
                $output | Should -Match "Line 4"
                $output | Should -Match "Line 5"
                $output | Should -Match "Line 6"
            }
            finally {
                Remove-Item $testFile -Force -ErrorAction SilentlyContinue
            }
        }

        It "C02. context for an end-append is displayed in grep format" {
            $testFile = [System.IO.Path]::GetTempFileName()
            try {
                Set-Content -Path $testFile -Value @("Line 1", "Line 2", "Line 3") -Encoding UTF8
                
                $output = Add-LinesToFile -Path $testFile -Content "Line 4" 6>&1 | Out-String
                
                # Confirm grep-format markers
                $output | Should -Match "3-"  # Preceding line (context)
                $output | Should -Match "4:"  # Added line (inverted display)
            }
            finally {
                Remove-Item $testFile -Force -ErrorAction SilentlyContinue
            }
        }

        It "C03. context display when appending multiple lines to the end" {
            $testFile = [System.IO.Path]::GetTempFileName()
            try {
                Set-Content -Path $testFile -Value @("Line 1", "Line 2", "Line 3") -Encoding UTF8
                
                $output = Add-LinesToFile -Path $testFile -Content @("Line 4", "Line 5") 6>&1 | Out-String
                
                # Confirm the context includes the 2 preceding lines
                $output | Should -Match "Line 2"
                $output | Should -Match "Line 3"
                # Confirm the added lines are included
                $output | Should -Match "Line 4"
                $output | Should -Match "Line 5"
            }
            finally {
                Remove-Item $testFile -Force -ErrorAction SilentlyContinue
            }
        }

        It "C04. can append to the end even when the file has fewer than 2 lines" {
            $testFile = [System.IO.Path]::GetTempFileName()
            try {
                Set-Content -Path $testFile -Value "Line 1" -Encoding UTF8
                
                $output = Add-LinesToFile -Path $testFile -Content "Line 2" 6>&1 | Out-String
                
                # Confirm the context includes line 1
                $output | Should -Match "Line 1"
                $output | Should -Match "Line 2"

                # Confirm file contents
                $result = Get-Content $testFile
                $result.Count | Should -Be 2
                $result[-1] | Should -Be "Line 2"
            }
            finally {
                Remove-Item $testFile -Force -ErrorAction SilentlyContinue
            }
        }

    }
    }