param(
    [string]$ZigPath = '',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist/ShakeFH2')
)
$ErrorActionPreference = 'Stop'
if (!$ZigPath) {
    $zigCommand = Get-Command zig -ErrorAction SilentlyContinue
    if ($zigCommand) { $ZigPath = $zigCommand.Source }
}
if (!$ZigPath -or !(Test-Path -LiteralPath $ZigPath)) { throw 'Zig required. Pass -ZigPath. Verified: Zig 0.16.0.' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework x64 C# compiler not found.' }
$dependencyFolder = Join-Path $PSScriptRoot 'dev'
New-Item -ItemType Directory -Path $dependencyFolder -Force | Out-Null
$nativeClient = Join-Path $dependencyFolder 'ViGEmClient.dll'
$expectedHash = '00303F99E1968D523FBD08A7C691D60D145B7335059EC593947841D124C10875'
if (!(Test-Path -LiteralPath $nativeClient) -or (Get-FileHash -LiteralPath $nativeClient -Algorithm SHA256).Hash -ne $expectedHash) {
    $package = Join-Path $dependencyFolder 'nefarius.vigem.client.1.21.256.nupkg'
    Invoke-WebRequest -Uri 'https://api.nuget.org/v3-flatcontainer/nefarius.vigem.client/1.21.256/nefarius.vigem.client.1.21.256.nupkg' -OutFile $package -TimeoutSec 60
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($package)
    try {
        $entry = $zip.GetEntry('lib/netstandard2.0/Nefarius.ViGEm.Client.dll')
        $stream = $entry.Open()
        $memory = New-Object IO.MemoryStream
        try {
            $stream.CopyTo($memory)
            $assembly = [Reflection.Assembly]::Load($memory.ToArray())
            $resource = $assembly.GetManifestResourceStream('costura64.vigemclient.dll')
            $output = New-Object IO.MemoryStream
            try { $resource.CopyTo($output); [IO.File]::WriteAllBytes($nativeClient, $output.ToArray()) }
            finally { $resource.Dispose(); $output.Dispose() }
        } finally { $stream.Dispose(); $memory.Dispose() }
    } finally { $zip.Dispose() }
    if ((Get-FileHash -LiteralPath $nativeClient -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Client resource hash mismatch.' }
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
& $ZigPath cc -target x86_64-windows-gnu -shared -O2 -o (Join-Path $OutputDirectory 'XboxTriggerOutput.dll') (Join-Path $PSScriptRoot 'src/native-output.c')
if ($LASTEXITCODE -ne 0) { throw 'Native compilation failed.' }
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /main:FH2Tools.Program /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll "/resource:$nativeClient,FH2Tools.ViGEmClient.x64" "/out:$OutputDirectory/ShakeFH2.exe" $sources
if ($LASTEXITCODE -ne 0) { throw 'C# compilation failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'DISTRIBUTION_README.md') -Destination (Join-Path $OutputDirectory 'README.md') -Force
Write-Output "Build completed: $OutputDirectory"
