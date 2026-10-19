#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Install-OmniDeskAgent.ps1'), [ref]$null, [ref]$errors)
if ($errors.Count) { throw 'Installer parse failed.' }
$outer = $ast.Find({param($node) $node -is [Management.Automation.Language.TryStatementAst] -and $node.Finally -and $node.Finally.Extent.Text.Contains('$stagedToken, $stagedCa')}, $true)
if (!$outer) { throw 'Installer cleanup not found.' }
$cleanup = [scriptblock]::Create($outer.Finally.Extent.Text)
foreach ($keepPackage in @($false, $true)) {
    $root = Join-Path $env:RUNNER_TEMP ('source-cleanup-test-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory $root | Out-Null
    $stagedMsi = Join-Path $root 'agent.msi'
    $stagedToken = Join-Path $root 'enrollment.txt'
    $stagedCa = Join-Path $root 'root.crt'
    try {
        foreach ($path in @($stagedMsi,$stagedToken,$stagedCa)) { [IO.File]::WriteAllText($path,'fixture') }
        . $cleanup
        if ((Test-Path $stagedToken) -or (Test-Path $stagedCa)) { throw 'Sensitive staging not removed.' }
        if ((Test-Path $stagedMsi) -ne $keepPackage) { throw 'MSI source retention incorrect.' }
        if ((Test-Path $root) -ne $keepPackage) { throw 'Source directory retention incorrect.' }
    } finally { if (Test-Path $root) { Remove-Item $root -Recurse -Force } }
}
Write-Host 'PASS installer cleanup: retain source after MSI attempt; remove token/CA; remove unused source before attempt.'
