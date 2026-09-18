$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
# Always build first. Never package an executable from an earlier build.
& .\build.ps1
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archivePath = Join-Path $PSScriptRoot 'artifacts\Insomnia-Fixed-Release.zip'
$stream = [System.IO.File]::Open($archivePath, [System.IO.FileMode]::Create)
$zip = New-Object System.IO.Compression.ZipArchive($stream, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    # Place executable and config directly in the root of the archive for easy double-click launch
    foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'artifacts\Release') -File | Where-Object { $_.Extension -in @('.exe', '.config') })) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $file.Name) | Out-Null
    }
    $readme = Join-Path $PSScriptRoot 'README.md'
    if (Test-Path $readme) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $readme, 'README.md') | Out-Null
    }
} finally { $zip.Dispose(); $stream.Dispose() }

$releaseNamedZip = Join-Path $PSScriptRoot 'artifacts\Insomnia-v1.0.0.zip'
Copy-Item $archivePath $releaseNamedZip -Force

Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
