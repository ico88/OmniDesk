#requires -Version 5.1
# Isolated Windows runner only. Uses a disposable modified MSI to force rollback.
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$BaselineMsi,[Parameter(Mandatory=$true)][string]$NewMsi)
$ErrorActionPreference='Stop'
$baseline=(Resolve-Path $BaselineMsi).Path
$current=(Resolve-Path $NewMsi).Path
if ($baseline.Contains('"') -or $current.Contains('"')) {throw 'Percorso MSI non valido.'}
if (Get-Service OmniDeskAgent -ErrorAction SilentlyContinue) {throw 'Runner non pulito.'}
$installer=New-Object -ComObject WindowsInstaller.Installer
function Get-ProductState([string]$code) {
    # Use a fresh automation object: Installer caches product registration state.
    $query=New-Object -ComObject WindowsInstaller.Installer
    try { return $query.GetType().InvokeMember('ProductState',[Reflection.BindingFlags]::GetProperty,$null,$query,@($code)) }
    finally { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($query) | Out-Null }
}
function Get-ProductCode([string]$path) {
    $db=$installer.OpenDatabase($path,0)
    $view=$db.OpenView("SELECT ``Value`` FROM ``Property`` WHERE ``Property``='ProductCode'")
    $null=$view.Execute()
    $row=$view.Fetch()
    $code=[string]$row.GetType().InvokeMember('StringData',[Reflection.BindingFlags]::GetProperty,$null,$row,@(1))
    $null=$view.Close()
    foreach ($com in @($row,$view,$db)) { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($com) | Out-Null }
    if ($code -notmatch '^\{[A-Fa-f0-9-]{36}\}$') { throw 'ProductCode MSI non valido.' }
    return $code
}
function Install-Package([string]$path,[string]$operation,[string]$name) {
    $log=Join-Path (Get-Location) "artifacts/msi-$name.log"
    $p=Start-Process msiexec.exe -ArgumentList @($operation,('"'+$path+'"'),'/qn','/norestart','/l*v',('"'+$log+'"')) -PassThru
    if (!$p.WaitForExit(180000)) { $p.Kill(); throw ('MSI test timeout: ' + $name) }
    $p.Refresh()
    return $p.ExitCode
}
function Assert-Installed([switch]$RequireRemote) {
    $deadline=[DateTime]::UtcNow.AddSeconds(45)
    do {Start-Sleep -Seconds 1;$s=Get-Service OmniDeskAgent -ErrorAction SilentlyContinue} while ((!$s -or $s.Status -ne 'Running') -and [DateTime]::UtcNow -lt $deadline)
    if (!$s -or $s.Status -ne 'Running') {throw 'Servizio non attivo dopo upgrade/repair.'}
    if ($RequireRemote) {
        $remote=Get-CimInstance Win32_Service -Filter "Name='OmniDeskRemote'"
        if (!$remote -or $remote.StartMode -ne 'Manual' -or $remote.StartName -ne 'LocalSystem') {throw 'Servizio remoto perso dopo upgrade/repair.'}
        $exe=Join-Path $env:ProgramFiles 'OmniDesk Agent/Remote/MeshService64.exe'
        if (!(Test-Path $exe) -or (Get-FileHash $exe -Algorithm SHA256).Hash -ne 'CBF10A37FC01F74F2473AECA0A4B391DCA6F9D0A8D0300AA7181DBCE7BACC51E') {throw 'Payload remoto perso/alterato dopo upgrade/repair.'}
    }
}
$oldCode=Get-ProductCode $baseline
$newCode=Get-ProductCode $current
$originalHash=(Get-FileHash $current -Algorithm SHA256).Hash
try {
    if ((Install-Package $baseline '/i' 'baseline') -notin @(0,3010)) {throw 'Installazione baseline fallita.'}
    Assert-Installed
    $state=Join-Path $env:ProgramData 'OmniDesk'
    New-Item -ItemType Directory -Path $state -Force | Out-Null
    $acl=[Security.AccessControl.DirectorySecurity]::new()
    $acl.SetSecurityDescriptorSddlForm('D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)')
    Set-Acl $state $acl
    $marker=Join-Path $state 'upgrade-fixture.txt'
    [IO.File]::WriteAllText($marker,'isolated-upgrade-fixture')
    if ((Install-Package $current '/i' 'upgrade') -notin @(0,3010)) {throw 'Upgrade fallito.'}
    Assert-Installed -RequireRemote
    $oldState=Get-ProductState $oldCode
    if ($oldState -ne -1) {throw ('Baseline ancora registrata dopo major upgrade: ' + $oldCode + ' state=' + $oldState)}
    if ([IO.File]::ReadAllText($marker) -ne 'isolated-upgrade-fixture') {throw 'Stato locale perso durante upgrade.'}
    & sc.exe sdset OmniDeskAgent 'D:P(A;;0xF01FF;;;SY)(A;;0xF01FF;;;BA)(A;;0xF01FF;;;IU)' | Out-Null
    if ($LASTEXITCODE -ne 0) {throw 'Preparazione fixture ACL fallita.'}
    # Reproduce the real client symptom on the disposable runner only.
    & sc.exe delete OmniDeskRemote | Out-Null
    if ($LASTEXITCODE -ne 0) {throw 'Preparazione fixture servizio mancante fallita.'}
    Remove-Item (Join-Path $env:ProgramFiles 'OmniDesk Agent/Remote/MeshService64.exe')
    if ((Install-Package $current '/fa' 'repair') -notin @(0,3010)) {throw 'Repair fallito.'}
    Assert-Installed -RequireRemote
    $sddl=((& sc.exe sdshow OmniDeskAgent) | Where-Object {$_ -match '^D:'}) -join ''
    $descriptor=[Security.AccessControl.CommonSecurityDescriptor]::new($false,$false,$sddl)
    foreach($ace in $descriptor.DiscretionaryAcl) {
        if ($ace.AceQualifier -eq 'AccessAllowed' -and $ace.SecurityIdentifier.Value -notin @('S-1-5-18','S-1-5-32-544') -and ($ace.AccessMask -band 0x500D0172) -ne 0) {throw 'Repair non ripristina i limiti del servizio.'}
    }
    if ([IO.File]::ReadAllText($marker) -ne 'isolated-upgrade-fixture') {throw 'Stato locale perso durante repair.'}
    if ((Install-Package $current '/x' 'before-rollback') -notin @(0,3010)) {throw 'Uninstall prima del test rollback fallito.'}
    $fault=Join-Path $env:RUNNER_TEMP 'OmniDesk.rollback-fixture.msi'
    Copy-Item $current $fault
    $db=$installer.OpenDatabase($fault,1)
    function Sql([string]$text) {$v=$db.OpenView($text);try {$null=$v.Execute()} finally {$null=$v.Close();[Runtime.InteropServices.Marshal]::FinalReleaseComObject($v) | Out-Null}}
    $command=(Join-Path ([Environment]::SystemDirectory) 'cmd.exe').Replace("'","''")
    Sql "INSERT INTO ``Property`` (``Property``,``Value``) VALUES ('OmniDeskTestExe','$command')"
    Sql "INSERT INTO ``CustomAction`` (``Action``,``Type``,``Source``,``Target``) VALUES ('OmniDeskTestRollback',3122,'OmniDeskTestExe','/d /c exit 1')"
    Sql "INSERT INTO ``InstallExecuteSequence`` (``Action``,``Condition``,``Sequence``) VALUES ('OmniDeskTestRollback','NOT Installed',6500)"
    $db.Commit();[Runtime.InteropServices.Marshal]::FinalReleaseComObject($db) | Out-Null;$db=$null
    if ((Install-Package $fault '/i' 'rollback') -ne 1603) {throw 'Installazione fault fixture non fallita come atteso.'}
    foreach($name in @('OmniDeskAgent','OmniDeskRemote')) {
        if(Get-Service $name -ErrorAction SilentlyContinue) {throw 'Servizio residuo dopo rollback.'}
    }
    if(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' -Name OmniDeskTray -ErrorAction SilentlyContinue) {throw 'Startup Tray residuo dopo rollback.'}
    if((Get-ProductState $newCode) -ne -1) {throw 'Prodotto registrato dopo rollback.'}
    if((Get-FileHash $current -Algorithm SHA256).Hash -ne $originalHash) {throw 'Artifact originale modificato.'}
    Write-Host 'PASS MSI: major upgrade 0.6, preserved local state, repair restores service ACL, deferred failure rolls back services/startup. Delivery MSI unchanged.'
}
finally {
    $null=Install-Package $current '/x' 'cleanup-current'
    $null=Install-Package $baseline '/x' 'cleanup-baseline'
}
