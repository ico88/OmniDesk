#Requires -Version 5.1
$ErrorActionPreference='Stop'
$folder=Join-Path $env:RUNNER_TEMP ('update-policy-test-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $folder | Out-Null
$script=Join-Path $folder 'fixed-fixture.ps1'
try {
    [IO.File]::WriteAllText($script,"Write-Output 'fixture-ok'")
    $exe=Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
    $start=[Diagnostics.ProcessStartInfo]::new($exe)
    $start.UseShellExecute=$false;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    $start.Arguments='-NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File "'+$script+'"'
    # Simulate a restrictive inherited process policy; the explicit child process setting wins.
    $start.EnvironmentVariables['PSExecutionPolicyPreference']='Restricted'
    $p=[Diagnostics.Process]::Start($start)
    $output=$p.StandardOutput.ReadToEnd();$errors=$p.StandardError.ReadToEnd()
    if (!$p.WaitForExit(15000)) {$p.Kill();throw 'PowerShell fixture timeout.'}
    if ($p.ExitCode -ne 0 -or $output.Trim() -ne 'fixture-ok') {throw 'Fixed local script launch denied or incorrect.'}
    Write-Host 'PASS fixed local PowerShell process policy; no Windows Update calls, global policy or service changes.'
} finally { Remove-Item $folder -Recurse -Force }
