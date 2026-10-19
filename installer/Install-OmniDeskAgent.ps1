#Requires -Version 5.1
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$MsiPath,
    [string]$ServerUrl = 'https://192.168.1.233:8085',
    [Parameter(Mandatory = $true)][string]$CaCertificatePath,
    [Parameter(Mandatory = $true)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$CaSha256,
    [string]$EnrollmentTokenFile,
    [switch]$TrustServerCa,
    [switch]$AllowUnsignedForTesting,
    [ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$MsiSha256
)
$ErrorActionPreference = 'Stop'
if (-not [Environment]::Is64BitOperatingSystem -or -not [Environment]::Is64BitProcess) { throw 'Use 64-bit PowerShell on Windows x64.' }
$server = $null
if (-not [Uri]::TryCreate($ServerUrl, [UriKind]::Absolute, [ref]$server) -or $server.Scheme -ne 'https' -or
    $server.UserInfo -or $server.Query -or $server.Fragment -or $server.AbsolutePath -ne '/') {
    throw 'ServerUrl must be an HTTPS origin, for example https://192.168.1.233:8085'
}
$root = Join-Path $env:ProgramData ('OmniDesk-Install-' + [Guid]::NewGuid().ToString('N'))
$directory = [IO.Directory]::CreateDirectory($root)
$acl = New-Object Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true, $false)
$admin = New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')
$system = New-Object Security.Principal.SecurityIdentifier('S-1-5-18')
$acl.SetOwner($admin)
foreach ($sid in @($admin, $system)) {
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
}
Set-Acl -LiteralPath $root -AclObject $acl
$stagedMsi = Join-Path $root 'agent.msi'
$stagedCa = Join-Path $root 'root.crt'
$stagedToken = Join-Path $root 'enrollment.txt'
try {
    [IO.File]::Copy((Resolve-Path -LiteralPath $MsiPath).Path, $stagedMsi, $false)
    [IO.File]::Copy((Resolve-Path -LiteralPath $CaCertificatePath).Path, $stagedCa, $false)
    $msiHash = (Get-FileHash -LiteralPath $stagedMsi -Algorithm SHA256).Hash
    if ($MsiSha256 -and $msiHash -ne $MsiSha256) { throw 'MSI SHA256 mismatch.' }
    $signature = Get-AuthenticodeSignature -LiteralPath $stagedMsi
    if ($signature.Status -ne 'Valid') {
        if (-not $AllowUnsignedForTesting -or -not $MsiSha256 -or $signature.Status -ne 'NotSigned') {
            throw 'A valid signed MSI is required. An unsigned lab package requires -AllowUnsignedForTesting and a separately verified -MsiSha256.'
        }
        Write-Warning 'Unsigned lab MSI explicitly accepted by SHA256. Not a production release.'
    }
    $certificate = New-Object Security.Cryptography.X509Certificates.X509Certificate2($stagedCa)
    try {
        $hash = [Security.Cryptography.SHA256]::Create()
        try { $actualCa = [BitConverter]::ToString($hash.ComputeHash($certificate.RawData)).Replace('-', '') } finally { $hash.Dispose() }
        if ($actualCa -ne $CaSha256) { throw 'CA SHA256 mismatch. Obtain the expected fingerprint independently from the server administrator.' }
        $constraints = $certificate.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.19' } | Select-Object -First 1
        if ($certificate.HasPrivateKey -or -not $constraints -or -not $constraints.CertificateAuthority -or
            $certificate.NotBefore.ToUniversalTime() -gt [DateTime]::UtcNow -or $certificate.NotAfter.ToUniversalTime() -le [DateTime]::UtcNow) {
            throw 'A valid public CA certificate without private key is required.'
        }
        $store = New-Object Security.Cryptography.X509Certificates.X509Store('Root', 'LocalMachine')
        try {
            $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
            $existing = $store.Certificates.Find([Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint, $certificate.Thumbprint, $false)
            if ($existing.Count -eq 0) {
                if (-not $TrustServerCa) { throw 'CA not trusted for the Windows service. Re-run with -TrustServerCa after independently verifying its SHA256.' }
                $store.Add($certificate)
                Write-Output 'Verified public CA installed in LocalMachine Root. Trust remains installed if subsequent steps fail.'
            }
        } finally { $store.Dispose() }
    } finally { $certificate.Dispose() }
    $identityPath = Join-Path $env:ProgramData 'OmniDesk\agent.json'
    if (Test-Path -LiteralPath $identityPath) {
        $identity = Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
        if ($identity.ServerUrl -ne $server.AbsoluteUri) { throw 'Existing enrollment uses another server. Revoke and migrate the identity explicitly.' }
    } else {
        if (-not $EnrollmentTokenFile) { throw 'EnrollmentTokenFile is required for a new device.' }
        [IO.File]::Copy((Resolve-Path -LiteralPath $EnrollmentTokenFile).Path, $stagedToken, $false)
        $token = [IO.File]::ReadAllText($stagedToken).Trim()
        if ($token -notmatch '^[A-Fa-f0-9]{64}$') { throw 'Invalid enrollment token file.' }
        $token = $null
    }
    $install = Start-Process -FilePath "$env:SystemRoot\System32\msiexec.exe" -ArgumentList @('/i', ('"' + $stagedMsi + '"'), '/qn', '/norestart') -Wait -PassThru
    if ($install.ExitCode -notin @(0, 3010)) { throw ('MSI installation failed: ' + $install.ExitCode) }
    if ($install.ExitCode -eq 3010) { Write-Warning 'Windows Installer reports a restart is required; no automatic restart was performed.' }
    $exe = Join-Path $env:ProgramFiles 'OmniDesk Agent\OmniDesk.Agent.Windows.exe'
    & $exe connect-test --server $server.AbsoluteUri
    if ($LASTEXITCODE -ne 0) { throw 'HTTPS readiness check failed. Enrollment not attempted.' }
    if (Test-Path -LiteralPath $stagedToken) {
        & $exe enroll --server $server.AbsoluteUri --token-file $stagedToken
        if ($LASTEXITCODE -ne 0) { throw 'Enrollment failed. Check server token consumption before retrying.' }
    }
    & $exe verify-enrollment --server $server.AbsoluteUri
    if ($LASTEXITCODE -ne 0) { throw 'Protected enrollment verification failed.' }
    $service = Get-Service -Name OmniDeskAgent
    if ($service.Status -ne 'Running') { Start-Service -Name OmniDeskAgent }
    Write-Output 'Agent installed/configured. Verify a real heartbeat and inventory in the dashboard; collection may take about a minute.'
    Write-Output 'Remove the original enrollment token file after successful enrollment. Temporary staging files are deleted; deletion is not a guarantee of secure disk erasure.'
} finally {
    foreach ($path in @($stagedToken, $stagedMsi, $stagedCa)) { if ([IO.File]::Exists($path)) { [IO.File]::Delete($path) } }
    if ([IO.Directory]::Exists($root)) { [IO.Directory]::Delete($root, $false) }
}
