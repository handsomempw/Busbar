param(
    [Parameter(Mandatory = $true)]
    [string]$IntermediateDir,

    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$ProjectDir = Split-Path $PSScriptRoot -Parent

$IntermediateDir = $IntermediateDir.Trim().Trim('"').TrimEnd('\', '/')
$normalizedProjectDir = $ProjectDir.Trim().TrimEnd('\', '/')

if ([string]::IsNullOrWhiteSpace($IntermediateDir)) {
    $IntermediateDir = Join-Path $ProjectDir ("obj\" + $Configuration)
}
elseif ($IntermediateDir -eq $normalizedProjectDir) {
    $IntermediateDir = Join-Path $ProjectDir ("obj\" + $Configuration)
}

function Get-GitExecutable {
    $command = Get-Command git -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidates = @(
        "$env:ProgramFiles\Git\cmd\git.exe",
        "$env:ProgramFiles(x86)\Git\cmd\git.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "git.exe was not found. Install Git for Windows or add Git cmd to PATH."
}

function Invoke-GitValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$GitExe,

        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $previousLocation = Get-Location
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        Set-Location -LiteralPath $WorkingDirectory
        $output = & $GitExe @Arguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        Set-Location $previousLocation
        $ErrorActionPreference = $previousErrorAction
    }

    if ($exitCode -ne 0) {
        $detail = ($output | Out-String).Trim()
        $commandText = $Arguments -join ' '
        throw "Git command failed: git $commandText. $detail"
    }

    $text = ($output | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) {
        return ""
    }

    return $text.Split([Environment]::NewLine)[0].Trim()
}

$gitExe = Get-GitExecutable
$version = Invoke-GitValue -GitExe $gitExe -WorkingDirectory $ProjectDir -Arguments @("log", "-1", "--format=%cd", "--date=format:%Y.%m.%d.%H%M")
$commitHash = Invoke-GitValue -GitExe $gitExe -WorkingDirectory $ProjectDir -Arguments @("rev-parse", "--short", "HEAD")

if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Version value from git is empty."
}

New-Item -ItemType Directory -Force -Path $IntermediateDir | Out-Null

$generatedVersionPath = Join-Path $IntermediateDir "GeneratedVersionInfo.cs"
$versionInfo = @"
using System.Reflection;

[assembly: AssemblyVersion("$version")]
[assembly: AssemblyFileVersion("$version")]
[assembly: AssemblyInformationalVersion("$version+$commitHash")]
"@

$utf8WithoutBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($generatedVersionPath, $versionInfo, $utf8WithoutBom)
