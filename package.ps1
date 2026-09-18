$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
# Always build first. Never package an executable from an earlier build.
& .\build.ps1
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archivePath = Join-Path $PSScriptRoot 'artifacts\Insomnia-Fixed-Release.zip'
$stream = [System.IO.File]::Open($archivePath, [System.IO.FileMode]::Create)
$zip = New-Object System.IO.Compression.ZipArchive($stream, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    $sourceFiles = Get-ChildItem -LiteralPath $PSScriptRoot -Recurse -File -Force | Where-Object {
        $relative = $_.FullName.Substring($PSScriptRoot.Length + 1)
        $relative -notmatch '(^|\\)(\.git|\.vs|bin|obj|artifacts)(\\|$)' -and
        ($_.Extension -in @('.cs','.csproj','.sln','.resx','.settings','.config','.ico','.jpg','.md','.ps1','.manifest') -or $_.Name -eq '.gitignore')
    }
    foreach ($file in $sourceFiles) {
        $name = 'Source/' + $file.FullName.Substring($PSScriptRoot.Length + 1).Replace('\','/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $name) | Out-Null
    }
    foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'artifacts\Release') -File)) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, ('Release/' + $file.Name)) | Out-Null
    }
} finally { $zip.Dispose(); $stream.Dispose() }
Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
