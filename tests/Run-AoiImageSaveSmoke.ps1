<#
.SYNOPSIS
Compile and run the AOI image archive smoke test against the built application and local HALCON.
.DESCRIPTION
Build BusbarCompressionSystem in Debug first. Tests use loopback TCP and a separate temporary output directory.
Example: powershell -ExecutionPolicy Bypass -File tests\Run-AoiImageSaveSmoke.ps1
#>
param()

$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$binaries = Join-Path $repository 'BusbarCompressionSystem\bin\Debug'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$installation = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath
$compiler = Join-Path $installation 'MSBuild\Current\Bin\Roslyn\csc.exe'
$probeDirectory = Join-Path ([IO.Path]::GetTempPath()) ('BusbarAoiImageSave-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDirectory | Out-Null
$probeExe = Join-Path $probeDirectory 'AoiImageSaveSmoke.exe'

Copy-Item -LiteralPath (Join-Path $binaries 'halcondotnet.dll') -Destination $probeDirectory
Copy-Item -LiteralPath (Join-Path $binaries 'halcon.dll') -Destination $probeDirectory
Copy-Item -LiteralPath (Join-Path $binaries 'hcanvas.dll') -Destination $probeDirectory
& $compiler /nologo /target:exe /platform:x64 "/out:$probeExe" "/reference:$binaries\halcondotnet.dll" /reference:System.Drawing.dll (Join-Path $PSScriptRoot 'AoiImageSaveSmoke.cs')
if ($LASTEXITCODE -ne 0) { throw 'AOI image save smoke test compilation failed.' }

& $probeExe $binaries (Join-Path $probeDirectory 'output')
if ($LASTEXITCODE -ne 0) { throw "AOI image save smoke test failed. Output: $probeDirectory" }
Write-Output "PASS. Images and logs: $probeDirectory\output"
