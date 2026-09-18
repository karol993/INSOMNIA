param([switch]$Test)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'Visual Studio MSBuild with .NET Framework 4.8 targeting pack is required.' }
& $msbuild Insomnia.sln /t:Rebuild /p:Configuration=Release /p:OutputPath=artifacts\Release\ /v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
if ($Test) {
    foreach ($architecture in @('x86', 'x64')) {
        & $msbuild Tests\Insomnia.Tests.csproj /t:Rebuild "/p:PlatformTarget=$architecture" /v:minimal
        if ($LASTEXITCODE -ne 0) { throw "Test build failed: $architecture" }
        & ".\Tests\bin\$architecture\Insomnia.Tests.exe"
        if ($LASTEXITCODE -ne 0) { throw "Tests failed: $architecture" }
    }
}
