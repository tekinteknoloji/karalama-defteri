param(
    [Parameter(Mandatory)][uri]$AssetUrl,
    [Parameter(Mandatory)][string]$Notes,
    [string]$MinVersion = '2.0.0',
    [string]$Output = 'dist/release',
    [string]$SignerScript,
    [string]$SignerThumbprint,
    [string]$SupportPaymentHost,
    [string]$SupportConfiguration,
    [switch]$GoogleOAuthTest,
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
if (!$AssetUrl.IsAbsoluteUri -or $AssetUrl.Scheme -ne 'https' -or $AssetUrl.UserInfo) { throw 'EXE adresi HTTPS olmalıdır.' }
if ($SignerThumbprint -and !$SignerScript) { throw 'Sertifika parmak izi için imzalama betiği gerekir.' }
$repo = 'https://github.com/tekinteknoloji/karalama-defteri/releases'
$googleOAuthPath = Join-Path $PSScriptRoot 'secrets/google-oauth.json'
$embedGoogleOAuth = Test-Path -LiteralPath $googleOAuthPath -PathType Leaf
if ($GoogleOAuthTest -and !$embedGoogleOAuth) { throw 'Google masaüstü OAuth dosyası bulunamadı.' }
if ($embedGoogleOAuth) {
    $installed = (Get-Content -LiteralPath $googleOAuthPath -Raw | ConvertFrom-Json).installed
    if ($installed.client_id -notmatch '^[A-Za-z0-9._-]+\.apps\.googleusercontent\.com$' -or
        [string]::IsNullOrWhiteSpace($installed.client_secret)) { throw 'Google masaüstü OAuth yapılandırması geçersiz.' }
}
$publish = @(
    'publish', (Join-Path $PSScriptRoot 'src/KaralamaDefteri'), '-c', 'Release', '-r', 'win-x64',
    '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableSingleFileAnalyzer=false',
    "-p:EmbedGoogleOAuth=$embedGoogleOAuth",
    "-p:StableUpdateFeed=$repo/latest/download/latest.json", "-p:BetaUpdateFeed=$repo/download/beta/latest.json",
    "-p:UpdateSignerThumbprint=$SignerThumbprint", "-p:SupportPaymentHost=$SupportPaymentHost", '-o', $Output
)
if ($NoRestore) { $publish += '--no-restore' }
dotnet @publish
if ($LASTEXITCODE) { throw 'Derleme başarısız.' }
$exe = Get-Item -LiteralPath (Join-Path $Output 'KaralamaDefteri.exe')
if ($SignerScript) {
    $global:LASTEXITCODE = 0
    & $SignerScript $exe.FullName
    if ($LASTEXITCODE) { throw 'İmzalama başarısız.' }
    if ((Get-AuthenticodeSignature -LiteralPath $exe.FullName).Status -ne 'Valid') { throw 'İmza doğrulanamadı.' }
}
$exe.Refresh()
$hash = (Get-FileHash -LiteralPath $exe.FullName -Algorithm SHA256).Hash
$manifest = [ordered]@{
    version = $exe.VersionInfo.ProductVersion
    url = $AssetUrl.AbsoluteUri
    sha256 = $hash
    size = $exe.Length
    notes = $Notes
    minVersion = $MinVersion
    homepage = 'https://tekinteknoloji.com/karalama-defteri/'
    privacyPolicy = 'https://tekinteknoloji.com/karalama-defteri/gizlilik/'
}
if ($SupportConfiguration) {
    $manifest.support = (Get-Content -LiteralPath $SupportConfiguration -Raw | ConvertFrom-Json).support
    if ($null -eq $manifest.support) { throw 'Destek yapılandırması eksik.' }
}
[System.IO.File]::WriteAllText((Join-Path $exe.DirectoryName 'latest.json'), ($manifest | ConvertTo-Json -Depth 5))
"$hash  $($exe.Name)" | Set-Content -LiteralPath (Join-Path $Output 'SHA256SUMS') -Encoding ascii
