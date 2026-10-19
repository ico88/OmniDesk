#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'Install-OmniDeskAgent.ps1'
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($source, [ref]$null, [ref]$errors)
if ($errors.Count) { throw 'Installer parse failed.' }
$branch = $ast.Find({param($node) $node -is [Management.Automation.Language.IfStatementAst] -and $node.Clauses[0].Item1.Extent.Text -eq '$EnrollmentTokenFile'}, $true)
if (-not $branch) { throw 'Token input branch not found.' }
$inputBlock = [scriptblock]::Create($branch.Extent.Text)
$stagedToken = Join-Path $env:RUNNER_TEMP ('enrollment-test-' + [Guid]::NewGuid().ToString('N'))
$EnrollmentTokenFile = $null
$script:reply = ('a' * 64)
function Read-Host { param([string]$Prompt,[switch]$AsSecureString) if (!$AsSecureString) {throw 'Token prompt must be masked.'}; ConvertTo-SecureString $script:reply -AsPlainText -Force }
try {
    . $inputBlock
    if ([IO.File]::ReadAllText($stagedToken) -ne $script:reply) { throw 'Pasted token not staged correctly.' }
    Remove-Item $stagedToken
    $script:reply = 'invalid'
    $rejected = $false
    try { . $inputBlock } catch { $rejected = $true }
    if (!$rejected -or (Test-Path $stagedToken)) {throw 'Invalid token was staged.'}
    Write-Host 'PASS masked enrollment prompt: valid paste staged; invalid token rejected before write.'
} finally {
    Remove-Item $stagedToken -ErrorAction SilentlyContinue
    $script:reply = $null
    Remove-Item Function:\Read-Host
}
