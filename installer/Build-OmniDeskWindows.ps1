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
    [xml]$setup = Get-Content 'src/OmniDesk.Setup.Windows/OmniDesk.Setup.Windows.csproj'
    if ($version -ne [string]($setup.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)) { throw 'Versioni Agent e Setup differenti.' }
    & npm ci --ignore-scripts --omit=dev --prefix modules/remote-meshcentral
    if ($LASTEXITCODE -ne 0) { throw 'Restore vendor remoto fallito.' }
    $remoteSource = 'modules/remote-meshcentral/node_modules/meshcentral/agents/MeshService64.exe'
    $remoteHash = (Get-FileHash $remoteSource -Algorithm SHA256).Hash
    if ($remoteHash -ne 'CBF10A37FC01F74F2473AECA0A4B391DCA6F9D0A8D0300AA7181DBCE7BACC51E') { throw 'Hash vendor remoto non corrispondente.' }
    New-Item -ItemType Directory -Path 'artifacts/remote-win-x64' -Force | Out-Null
    Copy-Item $remoteSource 'artifacts/remote-win-x64/MeshService64.exe'
    foreach ($component in @('Agent', 'Tray', 'Setup')) {
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
    Copy-Item 'artifacts/setup-win-x64/OmniDesk.Setup.exe' $delivery
    $manifest = @('OmniDesk.Agent.msi', 'OmniDesk.Setup.exe') | ForEach-Object {
        $hash = Get-FileHash (Join-Path $delivery $_) -Algorithm SHA256
        '{0}  {1}' -f $hash.Hash.ToLowerInvariant(), $_
    }
    $manifest | Set-Content (Join-Path $delivery 'SHA256SUMS.txt') -Encoding ASCII
    Write-Host "Pacchetto generato in $delivery. Non firmato: destinato al collaudo, non alla distribuzione di produzione."
    Write-Host 'La compilazione non installa servizi e non verifica desktop remoto o collegamento LAN.'
}
finally { Pop-Location }
