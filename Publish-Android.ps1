param(
    [string]$Output = 'dist/android-release',
    [string]$Version,
    [int]$VersionCode,
    [string]$Notes = 'Android güncellemesi.',
    [string]$MinVersion = '0.4.0',
    [string]$QaSource
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$localSdk = Join-Path $root '.sdk'
$jdk = Join-Path $localSdk 'java17-compatible'
$cert = Join-Path $root 'secrets/android-release.p12'
$protected = Join-Path $root 'secrets/android-release-password.dpapi'
$project = Join-Path $root 'src/KaralamaDefteri.Mobile/KaralamaDefteri.Mobile.csproj'
[xml]$projectMetadata = Get-Content -LiteralPath $project -Raw
$application = $projectMetadata.Project.PropertyGroup | Where-Object { $_.ApplicationDisplayVersion } | Select-Object -First 1
if (!$Version) { $Version = $application.ApplicationDisplayVersion }
if (!$VersionCode) { $VersionCode = [int]$application.ApplicationVersion }
$env:DOTNET_CLI_HOME = $localSdk
$env:NUGET_PACKAGES = Join-Path $root '.nuget'
$env:JAVA_HOME = $jdk
New-Item -ItemType Directory -Path (Split-Path $cert) -Force | Out-Null
Add-Type -AssemblyName System.Security.Cryptography.ProtectedData
$dataScope = [Security.Cryptography.DataProtectionScope]::CurrentUser
if (!(Test-Path -LiteralPath $cert) -and !(Test-Path -LiteralPath $protected)) {
    $random = [Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
    try { [IO.File]::WriteAllBytes($protected, [Security.Cryptography.ProtectedData]::Protect($random, $null, $dataScope)) }
    finally { [Security.Cryptography.CryptographicOperations]::ZeroMemory($random) }
}
if (!(Test-Path -LiteralPath $protected)) { throw 'Yayın anahtarı için DPAPI parola kaydı bulunamadı.' }
$secret = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($protected), $null, $dataScope)
try {
    $env:KARALAMA_ANDROID_SIGNING_PASSWORD = [Convert]::ToBase64String($secret)
    if (!(Test-Path -LiteralPath $cert)) {
        & "$jdk/bin/keytool.exe" -genkeypair -noprompt -storetype PKCS12 -keystore $cert -storepass:env KARALAMA_ANDROID_SIGNING_PASSWORD -alias karalama -keyalg RSA -keysize 3072 -validity 10950 -dname 'CN=Karalama Defteri, O=Tekin Teknoloji'
        if ($LASTEXITCODE) { throw 'Android yayın anahtarı oluşturulamadı.' }
    }
    $dotnet = Join-Path $localSdk 'dotnet.exe'
    $configuration = if ($QaSource) { 'Debug' } else { 'Release' }
    $options = @('build', $project, '-c', $configuration, '-m:1', '-nr:false', '-p:UseSharedCompilation=false',
        "-p:ApplicationDisplayVersion=$Version", "-p:ApplicationVersion=$VersionCode", '-p:AndroidKeyStore=true',
        "-p:AndroidSigningKeyStore=$cert", '-p:AndroidSigningKeyAlias=karalama',
        '-p:AndroidSigningStorePass=env:KARALAMA_ANDROID_SIGNING_PASSWORD',
        '-p:AndroidSigningKeyPass=env:KARALAMA_ANDROID_SIGNING_PASSWORD', '-p:NuGetAudit=false',
        '-o', $Output)
    if ($QaSource) { $options += "-p:AndroidUpdateQaSource=$QaSource" }
    & $dotnet @options
    if ($LASTEXITCODE) { throw 'İmzalı Android APK derlenemedi.' }
    $apk = Join-Path $Output 'com.tekinteknoloji.karalamadefteri-Signed.apk'
    $verified = & "$localSdk/android-sdk/build-tools/32.0.0/apksigner.bat" verify --print-certs $apk 2>$null
    if ($LASTEXITCODE) { throw 'APK imzası doğrulanamadı.' }
    $verified | Select-String 'Signer #1 certificate'
    if (!$QaSource) {
        $assetName = 'KaralamaDefteri-Android.apk'
        $publishedApk = Join-Path $Output $assetName
        Copy-Item -LiteralPath $apk -Destination $publishedApk
        $hash = (Get-FileHash -LiteralPath $publishedApk -Algorithm SHA256).Hash
        $manifest = [ordered]@{
            version = $Version
            url = "https://github.com/tekinteknoloji/karalama-defteri/releases/download/android-v$Version/$assetName"
            sha256 = $hash
            size = (Get-Item -LiteralPath $publishedApk).Length
            notes = $Notes
            minVersion = $MinVersion
        }
        $manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Output 'latest.json') -Encoding utf8
        "$hash  $assetName" | Set-Content -LiteralPath (Join-Path $Output 'SHA256SUMS') -Encoding ascii
    }
}
finally {
    Remove-Item Env:KARALAMA_ANDROID_SIGNING_PASSWORD -ErrorAction SilentlyContinue
    [Security.Cryptography.CryptographicOperations]::ZeroMemory($secret)
}
