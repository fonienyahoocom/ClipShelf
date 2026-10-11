$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$OriginalWorkingDirectory = (Get-Location).Path
$FailureLog = Join-Path $ScriptDir 'BUILD失敗_把這個傳給ChatGPT.txt'
$Ready = Join-Path $ScriptDir 'ClipShelf_READY'
$CoreProject = Join-Path $ScriptDir 'ClipShelf.Core\ClipShelf.Core.csproj'
$UiProject = Join-Path $ScriptDir 'ClipShelf\ClipShelf.csproj'
$HistoryFile = Join-Path $ScriptDir 'DEVELOPMENT_HISTORY.md'
$SuccessLog = Join-Path $Ready 'BUILD_SUCCESS.log'
$BuildFailed = $false
$FailureReason = ''

function Write-Log {
    param([Parameter(Mandatory=$true)][string]$Message)
    $stamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff'
    $line = "[$stamp] $Message"
    Write-Host $line
    [System.IO.File]::AppendAllText($FailureLog, $line + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($true))
}

function Invoke-LoggedNative {
    param(
        [Parameter(Mandatory=$true)][string]$Label,
        [Parameter(Mandatory=$true)][string]$FilePath,
        [Parameter(Mandatory=$true)][string[]]$Arguments,
        [switch]$AllowFailure
    )

    Write-Log "---- $Label ----"
    Write-Log ("COMMAND: `"{0}`" {1}" -f $FilePath, ($Arguments -join ' '))

    & $FilePath @Arguments 2>&1 | ForEach-Object {
        $text = $_.ToString()
        Write-Host $text
        [System.IO.File]::AppendAllText($FailureLog, $text + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($true))
    }
    $exitCode = $LASTEXITCODE
    Write-Log "EXIT CODE: $exitCode"

    if (($exitCode -ne 0) -and (-not $AllowFailure)) {
        throw "$Label failed with exit code $exitCode"
    }
    return $exitCode
}

function Test-RequiredFile {
    param([string]$Path, [string]$Description)
    $exists = Test-Path -LiteralPath $Path -PathType Leaf
    Write-Log ("CHECK {0}: {1} -> {2}" -f $Description, $Path, $exists)
    if (-not $exists) {
        throw "Missing required file: $Description ($Path)"
    }
}

# The fixed failure handoff file is the single authoritative build log.
# It is created before any project checks or dotnet commands.
if (Test-Path -LiteralPath $FailureLog) {
    Remove-Item -LiteralPath $FailureLog -Force -ErrorAction SilentlyContinue
}
[System.IO.File]::WriteAllText(
    $FailureLog,
    "ClipShelf BUILD HARNESS 2 - authoritative diagnostic log" + [Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($true)
)

try {
    Write-Log 'BUILD_EXE PowerShell harness started.'
    Write-Log 'ClipShelf package: v2.15.2 - Desktop Release - Build Harness 3'
    Write-Log "Original working directory: $OriginalWorkingDirectory"
    Write-Log "Script directory: $ScriptDir"
    Write-Log "PowerShell version: $($PSVersionTable.PSVersion)"
    Write-Log "PowerShell edition: $($PSVersionTable.PSEdition)"

    Set-Location -LiteralPath $ScriptDir
    Write-Log "Working directory after Set-Location: $((Get-Location).Path)"

    Test-RequiredFile -Path $CoreProject -Description 'ClipShelf Core project'
    Test-RequiredFile -Path $UiProject -Description 'ClipShelf UI project'

    $historyExists = Test-Path -LiteralPath $HistoryFile -PathType Leaf
    Write-Log "CHECK DEVELOPMENT_HISTORY.md: $HistoryFile -> $historyExists"

    $dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    }
    if ($null -eq $dotnetCommand) {
        throw '.NET SDK executable (dotnet) was not found in PATH.'
    }
    $Dotnet = $dotnetCommand.Source
    Write-Log "dotnet path: $Dotnet"

    [void](Invoke-LoggedNative -Label 'dotnet --info' -FilePath $Dotnet -Arguments @('--info'))

    # Remove only this package's previous READY output. If Windows says it is locked,
    # stop with a clear build failure rather than leaving a mixed old/new release.
    if (Test-Path -LiteralPath $Ready) {
        Write-Log "Removing previous READY folder: $Ready"
        Remove-Item -LiteralPath $Ready -Recurse -Force
    }
    New-Item -ItemType Directory -Path $Ready -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $Ready 'ui') -Force | Out-Null

    [void](Invoke-LoggedNative -Label 'Restore Core' -FilePath $Dotnet -Arguments @('restore', $CoreProject, '--ignore-failed-sources'))
    [void](Invoke-LoggedNative -Label 'Restore UI' -FilePath $Dotnet -Arguments @('restore', $UiProject, '--ignore-failed-sources'))

    [void](Invoke-LoggedNative -Label 'Publish Core' -FilePath $Dotnet -Arguments @(
        'publish', $CoreProject, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
        '-p:SelfContained=true', '-p:PublishSingleFile=false', '-p:PublishTrimmed=false', '-o', $Ready
    ))

    $UiOutput = Join-Path $Ready 'ui'
    [void](Invoke-LoggedNative -Label 'Publish UI' -FilePath $Dotnet -Arguments @(
        'publish', $UiProject, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
        '-p:SelfContained=true', '-p:PublishSingleFile=false', '-p:PublishTrimmed=false', '-o', $UiOutput
    ))

    $CoreExe = Join-Path $Ready 'ClipShelf.exe'
    $UiExe = Join-Path $UiOutput 'ClipShelf.UI.exe'
    Test-RequiredFile -Path $CoreExe -Description 'required ClipShelf.exe'
    Test-RequiredFile -Path $UiExe -Description 'required ClipShelf.UI.exe'

    # DEVELOPMENT_HISTORY is important for AI handoff, but it is not required for runtime.
    if ($historyExists) {
        Copy-Item -LiteralPath $HistoryFile -Destination (Join-Path $Ready 'DEVELOPMENT_HISTORY.md') -Force
        Write-Log 'DEVELOPMENT_HISTORY.md copied into READY.'
    } else {
        Write-Log 'WARNING: DEVELOPMENT_HISTORY.md is missing; application build remains usable.'
    }

    Write-Log 'FINAL STATUS: BUILD SUCCESS'
    Write-Log "Required artifact: $CoreExe"
    Write-Log "Required artifact: $UiExe"

    # Preserve the one authoritative log by moving it into the successful READY output.
    # No second copy is created.
    if (Test-Path -LiteralPath $SuccessLog) {
        Remove-Item -LiteralPath $SuccessLog -Force -ErrorAction SilentlyContinue
    }
    Move-Item -LiteralPath $FailureLog -Destination $SuccessLog -Force

    Write-Host ''
    Write-Host '================================================'
    Write-Host 'BUILD SUCCESS'
    Write-Host '================================================'
    Write-Host "Start: $CoreExe"
    Write-Host ''

    Start-Process explorer.exe -ArgumentList @($Ready)
    exit 0
}
catch {
    $BuildFailed = $true
    $FailureReason = $_.Exception.Message
    try {
        Write-Log "FINAL STATUS: BUILD FAILED"
        Write-Log "Failure reason: $FailureReason"
        Write-Log "Exception type: $($_.Exception.GetType().FullName)"
        Write-Log "Script stack trace: $($_.ScriptStackTrace)"
    } catch {
        # Last resort: the fixed file already exists from script start.
    }

    # Do not leave a mixed partial release after a required Core/UI failure.
    try {
        if (Test-Path -LiteralPath $Ready) {
            Remove-Item -LiteralPath $Ready -Recurse -Force -ErrorAction SilentlyContinue
        }
    } catch {}

    Write-Host ''
    Write-Host '================================================'
    Write-Host 'BUILD FAILED'
    Write-Host '================================================'
    Write-Host "Reason: $FailureReason"
    Write-Host '把這個檔案直接傳給 ChatGPT：'
    Write-Host $FailureLog
    Write-Host ''

    try {
        $content = Get-Content -LiteralPath $FailureLog -Raw -ErrorAction Stop
        $content | clip.exe
    } catch {}
    try {
        Start-Process notepad.exe -ArgumentList @($FailureLog)
    } catch {}

    exit 1
}
finally {
    try { Set-Location -LiteralPath $env:TEMP } catch {}
}
