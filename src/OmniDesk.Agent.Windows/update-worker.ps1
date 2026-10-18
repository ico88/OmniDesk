# Embedded, fixed script: accepts data only. Never execute a script delivered by the API.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$job = Get-Content -LiteralPath $env:OMNIDESK_UPDATE_INPUT -Raw | ConvertFrom-Json
try {
    $session = New-Object -ComObject Microsoft.Update.Session
    $session.ClientApplicationID = 'OmniDesk'
    $found = $session.CreateUpdateSearcher().Search("IsInstalled=0 and IsHidden=0 and Type='Software'")
    if ([int]$found.ResultCode -ne 2) { throw 'Update search did not complete successfully' }
    $list = @()
    for ($i = 0; $i -lt $found.Updates.Count; $i++) {
        $u = $found.Updates.Item($i)
        $list += @{ UpdateId=$u.Identity.UpdateID; Revision=[int]$u.Identity.RevisionNumber; Title=$u.Title.Substring(0,[Math]::Min(500,$u.Title.Length)); Kb=@($u.KBArticleIDs); Downloaded=[bool]$u.IsDownloaded; ResultCode=$null }
    }
    if ($job.Kind -eq 'scan') {
        if ($list.Count -gt 2000) { throw 'Too many updates; result not truncated silently' }
        @{ LeaseId=$job.LeaseId; Succeeded=$true; RebootRequired=$false; Updates=@($list); Error=$null } | ConvertTo-Json -Depth 8 -Compress
        exit 0
    }
    if ($job.Kind -ne 'install' -or @($job.Updates).Count -lt 1) { throw 'Unsupported job' }
    $collection = New-Object -ComObject Microsoft.Update.UpdateColl
    foreach ($wanted in $job.Updates) {
        $matched = $null
        for ($i = 0; $i -lt $found.Updates.Count; $i++) {
            $u = $found.Updates.Item($i)
            if ($u.Identity.UpdateID -eq $wanted.UpdateId -and $u.Identity.RevisionNumber -eq $wanted.Revision) { $matched=$u; break }
        }
        if ($null -eq $matched) { throw 'Approved update or revision no longer available; rescan required' }
        if (-not $matched.EulaAccepted) { $matched.AcceptEula() }
        [void]$collection.Add($matched)
    }
    $installer = $session.CreateUpdateInstaller()
    if ($installer.RebootRequiredBeforeInstallation) { throw 'Reboot required before installation; no automatic reboot performed' }
    $downloader = $session.CreateUpdateDownloader(); $downloader.Updates = $collection
    $download = $downloader.Download()
    if ([int]$download.ResultCode -ne 2) { throw 'Download failed or partially failed; installation not started' }
    $installer.Updates = $collection; $installer.AllowSourcePrompts = $false
    $installed = $installer.Install()
    $results = @()
    for ($i = 0; $i -lt $collection.Count; $i++) {
        $u=$collection.Item($i); $r=$installed.GetUpdateResult($i)
        $results += @{ UpdateId=$u.Identity.UpdateID; Revision=[int]$u.Identity.RevisionNumber; Title=$u.Title.Substring(0,[Math]::Min(500,$u.Title.Length)); Kb=@($u.KBArticleIDs); Downloaded=[bool]$u.IsDownloaded; ResultCode=[int]$r.ResultCode }
    }
    @{ LeaseId=$job.LeaseId; Succeeded=([int]$installed.ResultCode -eq 2); RebootRequired=[bool]$installed.RebootRequired; Updates=@($results); Error=$null } | ConvertTo-Json -Depth 8 -Compress
} catch {
    @{ LeaseId=$job.LeaseId; Succeeded=$false; RebootRequired=$false; Updates=@(); Error=('Windows Update error 0x{0:X8}' -f $_.Exception.HResult) } | ConvertTo-Json -Depth 8 -Compress
}
