#requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'La generazione MSI richiede Windows.' }
$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Installare .NET SDK 10 prima della compilazione.' }
    function Invoke-Dotnet {
        param([string[]]$Arguments)
        & dotnet @Arguments
        if ($LASTEXITCODE -ne 0) { throw "dotnet terminato con codice $LASTEXITCODE" }
    }
    [xml]$project = Get-Content 'src/OmniDesk.Agent.Windows/OmniDesk.Agent.Windows.csproj'
    $version = [string]($project.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
    [xml]$package = Get-Content 'installer/OmniDesk.Agent/Package.wxs'
    if ($version -ne [string]$package.Wix.Package.Version) { throw 'Versioni Agent e MSI differenti.' }
    [xml]$tray = Get-Content 'src/OmniDesk.Tray.Windows/OmniDesk.Tray.Windows.csproj'
    if ($version -ne [string]($tray.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)) { throw 'Versioni Agent e Tray differenti.' }
    foreach ($component in @('Agent', 'Tray')) {
        $output = "artifacts/$($component.ToLower())-win-x64"
        Invoke-Dotnet -Arguments @('publish', "src/OmniDesk.$component.Windows", '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-o', $output)
    }
    & '.\artifacts\agent-win-x64\OmniDesk.Agent.Windows.exe' self-test
    if ($LASTEXITCODE -ne 0) { throw 'Self-test Agent fallito.' }
    Invoke-Dotnet -Arguments @('build', 'installer/OmniDesk.Agent', '-c', 'Release')
    $msis = @(Get-ChildItem 'installer/OmniDesk.Agent/bin' -Filter 'OmniDesk.Agent.msi' -Recurse | Where-Object { $_.FullName -match '[\\/]Release[\\/]' })
    if ($msis.Count -ne 1) { throw 'Atteso un solo MSI Release; controllare le directory di compilazione.' }
    $delivery = Join-Path $repo "artifacts/windows-$version"
    New-Item -ItemType Directory -Path $delivery -Force | Out-Null
    Copy-Item $msis[0].FullName (Join-Path $delivery 'OmniDesk.Agent.msi')
    Copy-Item 'installer/Install-OmniDeskAgent.ps1' $delivery
    $manifest = @('OmniDesk.Agent.msi', 'Install-OmniDeskAgent.ps1') | ForEach-Object {
        $hash = Get-FileHash (Join-Path $delivery $_) -Algorithm SHA256
        '{0}  {1}' -f $hash.Hash.ToLowerInvariant(), $_
    }
    $manifest | Set-Content (Join-Path $delivery 'SHA256SUMS.txt') -Encoding ASCII
    Write-Host "Pacchetto generato in $delivery. Non firmato: destinato al collaudo, non alla distribuzione di produzione."
    Write-Host 'La compilazione non installa servizi e non verifica desktop remoto o collegamento LAN.'
}
finally { Pop-Location }
