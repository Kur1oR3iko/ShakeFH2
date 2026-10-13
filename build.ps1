param(
    [string]$ZigPath = '',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist\ShakeFH2')
)

$ErrorActionPreference = 'Stop'
if (!$ZigPath) {
    $zigCommand = Get-Command zig -ErrorAction SilentlyContinue
    if ($zigCommand) { $ZigPath = $zigCommand.Source }
}
if (!$ZigPath -or !(Test-Path -LiteralPath $ZigPath)) {
    throw 'Zig compiler not found. Put zig.exe on PATH or pass -ZigPath. Verified version: Zig 0.16.0.'
}
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework x64 C# compiler not found.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

& $ZigPath cc -target x86_64-windows-gnu -shared -O2 -o (Join-Path $OutputDirectory 'XboxTriggerOutput.dll') (Join-Path $PSScriptRoot 'native-output.c')
if ($LASTEXITCODE -ne 0) { throw 'Native output compilation failed.' }

& $compiler /nologo /target:winexe /platform:x64 /optimize+ /r:System.Windows.Forms.dll /r:System.Drawing.dll "/out:$OutputDirectory\ShakeFH2.exe" (Join-Path $PSScriptRoot 'XboxTriggerBridge.cs')
if ($LASTEXITCODE -ne 0) { throw 'C# compilation failed.' }

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'DISTRIBUTION_README.md') -Destination (Join-Path $OutputDirectory 'README.md') -Force
Write-Output "Build completed: $OutputDirectory"
