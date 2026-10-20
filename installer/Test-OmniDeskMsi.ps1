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
function Invoke-Msi {
    param([string[]]$Arguments,[string]$Operation)
    $process = Start-Process msiexec.exe -ArgumentList $Arguments -PassThru
    if (-not $process.WaitForExit(180000)) {
        try { $process.Kill() } catch { }
        throw "Timeout MSI durante: $Operation"
    }
    $process.Refresh()
    return $process.ExitCode
}
$installLog = Join-Path (Get-Location) 'artifacts/msi-install.log'
$uninstallLog = Join-Path (Get-Location) 'artifacts/msi-uninstall.log'
$attempted = $false
try {
    $attempted = $true
    $installExitCode = Invoke-Msi -Arguments @('/i',('"'+$msi+'"'),'/qn','/norestart','/l*v',('"'+$installLog+'"')) -Operation 'installazione'
    if ($installExitCode -notin @(0,3010)) { throw "Installazione MSI fallita: $installExitCode" }
    $service = Wait-Agent
    if ($service.StartMode -ne 'Auto' -or $service.StartName -ne 'LocalSystem') { throw 'Avvio o identita servizio non conformi.' }
    $directory = Join-Path $env:ProgramFiles 'OmniDesk Agent'
    foreach ($file in @('OmniDesk.Agent.Windows.exe','OmniDesk.Tray.Windows.exe')) {
        if (-not (Test-Path (Join-Path $directory $file))) { throw "Componente mancante: $file" }
    }
    Add-Type -AssemblyName System.Drawing
    $trayIcon = [System.Drawing.Icon]::ExtractAssociatedIcon((Join-Path $directory 'OmniDesk.Tray.Windows.exe'))
    if (-not $trayIcon -or $trayIcon.Size.Width -lt 16 -or $trayIcon.Size.Height -lt 16) { throw 'Icona prodotto OmniDesk non incorporata nella Tray.' }
    $trayIcon.Dispose()
    $remote = Get-CimInstance Win32_Service -Filter "Name='OmniDeskRemote'"
    if (-not $remote -or $remote.StartMode -ne 'Manual' -or $remote.State -ne 'Stopped' -or $remote.StartName -ne 'LocalSystem') { throw 'Servizio remoto non conforme o avviato senza approvazione.' }
    $remoteDirectory = Join-Path $directory 'Remote'
    $remoteExe = Join-Path $remoteDirectory 'MeshService64.exe'
    if ((Get-FileHash $remoteExe -Algorithm SHA256).Hash -ne 'CBF10A37FC01F74F2473AECA0A4B391DCA6F9D0A8D0300AA7181DBCE7BACC51E') { throw 'Vendor remoto non verificato.' }
    $folderAcl = Get-Acl $remoteDirectory
    foreach ($rule in $folderAcl.Access) {
        if ($rule.AccessControlType -eq 'Allow' -and $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value -notin @('S-1-5-18','S-1-5-32-544')) { throw 'Directory dati remoto leggibile da principal standard.' }
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
            ($ace.AccessMask -band 0x500D0172) -ne 0) { throw 'DACL concede controllo servizio a un principal standard.' }
    }
    foreach ($sid in @('S-1-5-18','S-1-5-32-544')) {
        $allowed = @($acl.DiscretionaryAcl | Where-Object { $_.AceQualifier -eq [Security.AccessControl.AceQualifier]::AccessAllowed -and $_.SecurityIdentifier.Value -eq $sid -and ($_.AccessMask -band 0xF01FF) -eq 0xF01FF })
        if ($allowed.Count -eq 0) { throw 'SYSTEM/Administrators non hanno SERVICE_ALL_ACCESS esplicito.' }
    }
    $agentController = Get-Service OmniDeskAgent
    $agentController.Stop()
    $agentController.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped,[TimeSpan]::FromSeconds(30))
    $fixtureId = [Guid]::NewGuid().ToString('N')
    $fixtureSettings = "MeshName=OmniDesk:$fixtureId`r`nMeshType=2`r`nMeshID=0x$('0'*96)`r`nServerID=$('0'*96)`r`nMeshServer=wss://127.0.0.1:9/agent.ashx`r`nmeshServiceName=OmniDeskRemote`r`ndisableUpdate=1`r`nTag=$fixtureId`r`n"
    [IO.File]::WriteAllText((Join-Path $remoteDirectory 'MeshService64.msh'),$fixtureSettings,[Text.Encoding]::ASCII)
    $remoteController = Get-Service OmniDeskRemote
    $remoteController.Start()
    $remoteController.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running,[TimeSpan]::FromSeconds(30))
    $node = ''
    $nodeDeadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        Start-Sleep -Seconds 2
        $node = (& $remoteExe -nodeid | Out-String).Trim()
    } while ($node -notmatch '^[0-9A-Fa-f]{96}$' -and [DateTime]::UtcNow -lt $nodeDeadline)
    if ($node -notmatch '^[0-9A-Fa-f]{96}$') { throw 'Identita nativa non generata o formato inatteso.' }
    $agentController = Get-Service OmniDeskAgent
    $agentController.Start()
    $agentController.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running,[TimeSpan]::FromSeconds(30))
    $stopDeadline = [DateTime]::UtcNow.AddSeconds(30)
    do { Start-Sleep -Seconds 1; $remoteState = (Get-Service OmniDeskRemote).Status } while ($remoteState -ne 'Stopped' -and [DateTime]::UtcNow -lt $stopDeadline)
    if ($remoteState -ne 'Stopped') { throw 'Agent non registrato non ha arrestato il motore non autorizzato.' }
    $service = Wait-Agent
    Write-Host 'PASS native engine: SCM startup, private data directory, hex node identity, fail closed without enrollment. No remote session tested.'
    $previous = [uint32]$service.ProcessId
    Stop-Process -Id $previous -Force
    $null = Wait-Agent -PreviousPid $previous
    Write-Host 'PASS MSI: Agent/Tray, icona prodotto, avvio automatico SYSTEM, startup Tray, DACL e recovery dopo crash.'
    Write-Host 'Non verificati: logon Tray, pipe interattiva, screenshot, enrollment LAN, remoto e upgrade.'
}
catch {
    if (Test-Path $installLog) { Get-Content $installLog -Tail 150 | Write-Host }
    Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=[DateTime]::Now.AddMinutes(-5)} -ErrorAction SilentlyContinue |
        Where-Object { $_.Message -match 'OmniDesk|OmniDeskAgent' } | Select-Object -First 5 |
        ForEach-Object { Write-Host $_.Message }
    throw
}
finally {
    if ($attempted) {
        $uninstallExitCode = Invoke-Msi -Arguments @('/x',('"'+$msi+'"'),'/qn','/norestart','/l*v',('"'+$uninstallLog+'"')) -Operation 'disinstallazione'
        if ($uninstallExitCode -notin @(0,1605,3010)) { throw "Disinstallazione MSI fallita: $uninstallExitCode" }
        if (Get-Service OmniDeskAgent -ErrorAction SilentlyContinue) { throw 'Servizio residuo dopo uninstall.' }
        if (Get-Service OmniDeskRemote -ErrorAction SilentlyContinue) { throw 'Servizio remoto residuo dopo uninstall.' }
        if (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' -Name OmniDeskTray -ErrorAction SilentlyContinue) { throw 'Startup Tray residuo dopo uninstall.' }
    }
}
