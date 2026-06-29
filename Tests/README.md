# PowerShell.MCP Tests

This directory contains the comprehensive test suite for the PowerShell.MCP project.

## 📁 Folder Structure

```
Tests/
├── Unit/                                      # C# unit tests
│   ├── Core/                                  # Core functionality tests
│   │   ├── TextFileUtilityTests.cs            # Utility class (20 tests)
│   │   ├── TextFileCmdletBaseTests.cs         # Base class (8 tests)
│   │   └── ValidationAttributesTests.cs       # Validation attributes (5 tests)
│   └── Cmdlets/                               # Per-cmdlet tests
│       ├── ShowTextFilesCmdletTests.cs         # Show-TextFiles (4 tests)
│       ├── AddLinesToFileCmdletTests.cs       # Add-LinesToFile (5 tests)
│       ├── UpdateLinesInFileCmdletTests.cs    # Update-LinesInFile (4 tests)
│       ├── RemoveLinesFromFileCmdletTests.cs  # Remove-LinesFromFile (3 tests)
│       ├── UpdateMatchInFileCmdletTests.cs    # Update-MatchInFile (4 tests)
│       └── TestTextFileContainsCmdletTests.cs # Test-TextFileContains (4 tests)
├── Integration/                               # PowerShell integration tests
│   ├── BlankLineSeparation.Tests.ps1          # Blank line separation tests
│   ├── ContextDisplay.Tests.ps1               # Context display tests
│   ├── ContextDisplay.EdgeCase.Tests.ps1      # Context display edge cases
│   ├── NetDisplay.Tests.ps1                   # net change display tests
│   ├── QuietErrorHandling.Tests.ps1           # Test-ThrowsQuietly practical examples
│   ├── TestThrowsQuietly.Tests.ps1            # Test-ThrowsQuietly function tests
│   └── ErrorOutputComparison.Tests.ps1        # Error output comparison tests
├── Manual/                                    # Manual tests
│   └── Show-TextFiles.Manual.Tests.ps1         # Show-TextFiles manual tests
├── TestData/                                  # Test data
│   ├── Encodings/                             # For encoding tests
│   └── Samples/                               # Sample files
├── Shared/                                    # Shared helpers
│   └── TestHelpers.psm1                       # PowerShell helper functions
│                                               # - New-TestFile
│                                               # - Remove-TestFile
│                                               # - Test-ThrowsQuietly
├── PowerShell.MCP.Tests.csproj
├── xunit.runner.json
├── README.md                                  # This file
├── COVERAGE.md                                # Coverage report
└── Run-AllTests.ps1                           # Test runner script
```

## 📊 Test Statistics

- **Total tests**: about 300+
- **Unit tests (C#)**: 96
- **Integration tests (PowerShell)**: 281
- **Pass rate**: 100% ✅

## 🎯 Test Strategy

### Unit Tests (C#)
- **Purpose**: Verify the behavior of individual methods/classes
- **Scope**: public/internal methods
- **Mocking**: Use Moq as needed
- **Speed**: Fast (< 100ms/test)

### Integration Tests (PowerShell)
- **Purpose**: Verify the actual behavior of cmdlets
- **Scope**: End-to-end behavior
- **Mocking**: None (uses real files)
- **Speed**: Medium (< 1s/test)

## 🚀 Running the Tests

### Run all tests
```powershell
.\Tests\Run-AllTests.ps1
```

### Run only C# unit tests (concise output)
```powershell
cd Tests
dotnet test --verbosity quiet --nologo
```

**If you need detailed output:**
```powershell
dotnet test --verbosity normal
```

### Run only PowerShell integration tests (concise output)
```powershell
# Pester 5.0+ is required
Install-Module -Name Pester -Force -SkipPublisherCheck -MinimumVersion 5.0.0

# Run integration tests (minimal output)
$config = New-PesterConfiguration
$config.Run.Path = ".\Tests\Integration"
$config.Output.Verbosity = "Minimal"
Invoke-Pester -Configuration $config
```

**If you need detailed output:**
```powershell
Invoke-Pester -Path .\Tests\Integration

### Run tests with concise error output
```powershell
# Filter error messages for more readable display
.\Tests\Invoke-PesterConcise.ps1

# Run only specific tests
.\Tests\Invoke-PesterConcise.ps1 -Path Integration/Cmdlets/Show-TextFiles.Tests.ps1

# Show help
Get-Help .\Tests\Invoke-PesterConcise.ps1 -Examples
```

**What is filtered:**
- Inner exception stack traces (`--->`, `--- End of`)
- `System.Management.Automation.*Exception` detail lines
- Duplicate exception messages
- Verbose stack trace lines
```

### Run with coverage
```powershell
cd Tests
dotnet test --collect:"XPlat Code Coverage"
```

## 🧪 Adding New Tests

### Adding a C# Unit Test

```csharp
// Tests/Unit/Cmdlets/NewCmdletTests.cs
using Xunit;
using PowerShell.MCP.Cmdlets;

namespace PowerShell.MCP.Tests.Unit.Cmdlets;

public class NewCmdletTests
{
    [Fact]
    public void MethodName_Condition_ExpectedBehavior()
    {
        // Arrange
        var cmdlet = new NewCmdlet();
        
        // Act
        var result = cmdlet.DoSomething();
        
        // Assert
        Assert.NotNull(result);
    }
}
```

### Adding a PowerShell Integration Test

```powershell
# Tests/Integration/Cmdlets/New-Cmdlet.Integration.Tests.ps1
#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

BeforeAll {
    Import-Module "$PSScriptRoot/../../Shared/TestHelpers.psm1" -Force
}

Describe "New-Cmdlet Integration Tests" {
    BeforeAll {
        $testFile = New-TestFile -Content "test"
    }

    AfterAll {
        Remove-TestFile -Path $testFile
    }

    Context "Basic functionality" {
        It "works as expected" {
            $result = New-Cmdlet -Path $testFile
            $result | Should -Not -BeNullOrEmpty
        }
    }
    
    Context "Error handling" {
        It "errors on a nonexistent file" {
            Test-ThrowsQuietly {
                New-Cmdlet -Path "C:\NonExistent\file.txt"
            } -ExpectedMessage "File not found"
        }
        
        It "errors on an invalid parameter" {
            Test-ThrowsQuietly {
                New-Cmdlet -Path $testFile -InvalidParam -999
            } -ExpectedMessage "less than the minimum"
        }
    }
}
```

**Important**: Always use `Test-ThrowsQuietly` for error-case tests. `Should -Throw` is not recommended because it generates a large amount of error output.

## 📚 Helper Functions

`Shared/TestHelpers.psm1` provides the following helper functions:

- `New-TestFile`: Create a temporary file for testing
- `Remove-TestFile`: Safely delete a test file
- `Test-ThrowsQuietly`: Verify an exception while completely suppressing error output (**recommended**)

### How to Use Test-ThrowsQuietly

**Purpose**: When verifying error cases in Pester tests, suppress the large volume of error messages and stack trace output, greatly reducing token consumption.

**Basic usage example:**
```powershell
# Conventional approach (produces a large amount of error output)
It "Should throw on missing file" {
    { Show-TextFiles -Path "missing.txt" } | Should -Throw
}

# Recommended approach (suppresses error output)
It "Should throw on missing file" {
    Test-ThrowsQuietly { Show-TextFiles -Path "missing.txt" }
}
```

**With message verification:**
```powershell
It "Should throw file not found error" {
    Test-ThrowsQuietly { 
        Show-TextFiles -Path "C:\NonExistent\file.txt" 
    } -ExpectedMessage "File not found"
}
```

**Complex error case:**
```powershell
It "Should throw on invalid LineRange" {
    $temp = New-TemporaryFile
    "test" | Out-File $temp
    try {
        Test-ThrowsQuietly {
            Show-TextFiles -Path $temp -LineRange @(10, 5)
        } -ExpectedMessage "must be less than or equal to"
    } finally {
        Remove-Item $temp -Force
    }
}
```

**Behavior details:**
- Runs `$Error.Clear()` twice, before and after the try, to clear the error history
- `*>&1` redirects all output streams
- `$null = ...` discards output completely
- `ErrorActionPreference = 'Stop'` converts non-terminating errors into exceptions
- `$PSDefaultParameterValues['*:ErrorAction'] = 'Stop'` automatically applies -ErrorAction Stop to all commands

**Error types covered:**
- ✅ Terminating errors (ThrowTerminatingError)
- ✅ Parameter validation errors (ValidateRange, etc.)
- ⚠️ Non-terminating errors (WriteError) - partially supported due to PowerShell and C# cmdlet limitations

## 🎯 Error Handling Best Practices

### How to Write Error Tests

**Recommended**: Use `Test-ThrowsQuietly` for all error tests.

```powershell
Describe "Error Handling Tests" {
    Context "Invalid parameters" {
        It "Negative LineNumber throws" {
            Test-ThrowsQuietly {
                Add-LinesToFile -Path "test.txt" -LineNumber -5 -Content "test"
            } -ExpectedMessage "less than the minimum"
        }
        
        It "Invalid LineRange throws" {
            Test-ThrowsQuietly {
                Show-TextFiles -Path "test.txt" -LineRange @(10, 5)
            } -ExpectedMessage "less than or equal to"
        }
    }
    
    Context "Missing files" {
        It "File not found throws" {
            Test-ThrowsQuietly {
                Show-TextFiles -Path "C:\NonExistent\file.txt"
            } -ExpectedMessage "File not found"
        }
    }
}
```

### Comparing Error Output

**Conventional Should -Throw:**
- Outputs a hundreds-to-thousands-character stack trace for each error
- Heavy token consumption
- Test results are hard to read

**Test-ThrowsQuietly:**
- Completely suppresses error output
- Greatly reduces token consumption (over 90% reduction)
- Test results are easy to read
- Verifies only whether an exception occurred and its message

### Real Examples

The Tests/Integration directory contains the following real examples:
- `QuietErrorHandling.Tests.ps1` - practical examples of Test-ThrowsQuietly
- `TestThrowsQuietly.Tests.ps1` - tests of the Test-ThrowsQuietly function itself
- `ErrorOutputComparison.Tests.ps1` - comparison with Should -Throw
## 🔧 Required Environment

- .NET 9.0 SDK
- PowerShell 7.2+
- xUnit (installed automatically)
- Moq (installed automatically)
- Pester 5.0+ (for integration tests)

## 📖 References

- [xUnit documentation](https://xunit.net/)
- [Moq documentation](https://github.com/moq/moq4)
- [Pester documentation](https://pester.dev/)
- [Coverage report](COVERAGE.md)

## License

This test suite is part of the PowerShell.MCP project and is governed by the same license.