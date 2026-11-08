# Builds "Hookshot Launcher.exe" from Launcher.cs with the .NET Framework's own compiler (on every Windows, nothing to install).
# signature.png is embedded; the icon is launcher.ico (the gold hookshot, make_icon.ps1).
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Add-Type -AssemblyName System.Drawing
$ico = Join-Path $here "launcher.ico"
# (the icon: the builder's hookshot in gold, made by make_icon.ps1)
if (-not (Test-Path $ico)) { & (Join-Path $here "make_icon.ps1") }
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$exe = Join-Path $here "Hookshot Launcher.exe"
& $csc /nologo /target:winexe /optimize+ /platform:anycpu "/out:$exe" "/win32icon:$ico" "/resource:$(Join-Path $here 'signature.png'),signature.png" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll (Join-Path $here "Launcher.cs")
if ($LASTEXITCODE -ne 0) { throw "csc failed" }
"built $exe ($((Get-Item $exe).Length) bytes)"
