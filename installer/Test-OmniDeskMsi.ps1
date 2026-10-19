#requires -Version 5.1
# Run only on an isolated Windows build runner. Installs then removes the package.
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$MsiPath)
$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Runner Windows elevato richiesto.' }
$msi = (Resolve-Path $MsiPath).Path
if ($msi.Contains('"')) { throw 'Percorso MSI non valido.' }
if (Get-Service OmniDeskAgent -ErrorAction SilentlyContinue) { throw 'Runner non pulito: servizio gia presente.' }
function Wait-Agent {
    param([uint32]$PreviousPid = 0)
    $limit = [DateTime]::UtcNow.AddSeconds(60)
    do {
        $service = Get-CimInstance Win32_Service -Filter "Name='OmniDeskAgent'"
        if ($service -and $service.State -eq 'Running' -and $service.ProcessId -ne 0 -and $service.ProcessId -ne $PreviousPid) { return $service }
        Start-Sleep -Seconds 1
    } while ([DateTime]::UtcNow -lt $limit)
    throw 'Servizio non avviato o non recuperato dopo crash.'
}
$attempted = $false
try {
    $attempted = $true
    $install = Start-Process msiexec.exe -ArgumentList @('/i',('"'+$msi+'"'),'/qn','/norestart') -Wait -PassThru
    if ($install.ExitCode -notin @(0,3010)) { throw "Installazione MSI fallita: $($install.ExitCode)" }
    $service = Wait-Agent
    if ($service.StartMode -ne 'Auto' -or $service.StartName -ne 'LocalSystem') { throw 'Avvio o identita servizio non conformi.' }
    $directory = Join-Path $env:ProgramFiles 'OmniDesk Agent'
    foreach ($file in @('OmniDesk.Agent.Windows.exe','OmniDesk.Tray.Windows.exe')) {
        if (-not (Test-Path (Join-Path $directory $file))) { throw "Componente mancante: $file" }
    }
    $run = Get-ItemPropertyValue 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' -Name OmniDeskTray
    if ($run -ne ('"'+(Join-Path $directory 'OmniDesk.Tray.Windows.exe')+'"')) { throw 'Startup Tray non conforme.' }
    $output = & sc.exe sdshow OmniDeskAgent
    if ($LASTEXITCODE -ne 0) { throw 'DACL servizio non leggibile.' }
    $sddl = ($output | Where-Object { $_ -match '^D:' }) -join ''
    $acl = [Security.AccessControl.CommonSecurityDescriptor]::new($false,$false,$sddl)
    foreach ($ace in $acl.DiscretionaryAcl) {
        if ($ace.AceQualifier -eq [Security.AccessControl.AceQualifier]::AccessAllowed -and
            $ace.SecurityIdentifier.Value -notin @('S-1-5-18','S-1-5-32-544') -and
            ($ace.AccessMask -band 0xD0172) -ne 0) { throw 'DACL concede controllo servizio a un principal standard.' }
    }
    $previous = [uint32]$service.ProcessId
    Stop-Process -Id $previous -Force
    $null = Wait-Agent -PreviousPid $previous
    Write-Host 'PASS MSI: Agent/Tray, avvio automatico SYSTEM, startup Tray, DACL e recovery dopo crash.'
    Write-Host 'Non verificati: logon Tray, pipe interattiva, screenshot, enrollment LAN, remoto e upgrade.'
}
finally {
    if ($attempted) {
        $uninstall = Start-Process msiexec.exe -ArgumentList @('/x',('"'+$msi+'"'),'/qn','/norestart') -Wait -PassThru
        if ($uninstall.ExitCode -notin @(0,1605,3010)) { throw "Disinstallazione MSI fallita: $($uninstall.ExitCode)" }
        if (Get-Service OmniDeskAgent -ErrorAction SilentlyContinue) { throw 'Servizio residuo dopo uninstall.' }
        if (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' -Name OmniDeskTray -ErrorAction SilentlyContinue) { throw 'Startup Tray residuo dopo uninstall.' }
    }
}
