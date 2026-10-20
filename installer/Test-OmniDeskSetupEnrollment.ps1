#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$source = Get-Content (Join-Path $PSScriptRoot '../src/OmniDesk.Setup.Windows/Program.cs') -Raw
$prompt = $source.IndexOf('MessageBoxButtons.YesNoCancel', [StringComparison]::Ordinal)
$revoke = $source.IndexOf('RunAsync(Agent, ["revoke"])', [StringComparison]::Ordinal)
$enroll = $source.IndexOf('RunAsync(Agent, ["enroll"', [StringComparison]::Ordinal)
if ($prompt -lt 0) { throw 'Setup does not ask how to handle an existing identity.' }
if ($revoke -lt $prompt -or $enroll -lt $revoke) { throw 'Replacement must prompt, revoke the old identity, then enroll.' }
if (-not $source.Contains('L''identità locale è stata conservata.')) { throw 'Failed revocation must explain that the local identity remains.' }
if (-not $source.Contains('MessageBoxButtons.YesNoCancel')) { throw 'Setup must offer keep, replace and cancel choices.' }
if ($source.Contains('File.Delete(Identity)')) { throw 'Setup must not delete a local identity without server revocation.' }
Write-Host 'PASS Setup existing identity prompt, server revocation and safe failure behavior.'
