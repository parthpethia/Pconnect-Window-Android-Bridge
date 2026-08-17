$ErrorActionPreference = 'Stop'

Write-Host "Building Android Mobile App (Flutter)..."

$env:GRADLE_USER_HOME = "d:\Projects\Pconnect\.gradle-user-home"

if (Test-Path "C:\Program Files\Android\Android Studio\jbr") {
    $env:JAVA_HOME = "C:\Program Files\Android\Android Studio\jbr"
    $env:Path = "$env:JAVA_HOME\bin;$env:Path"
}

$mobileDir = Join-Path $PSScriptRoot '..\mobile\pconnect_mobile'
$outputDir = Join-Path $PSScriptRoot '..\releases'
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}

Push-Location $mobileDir
try {
    flutter pub get
    flutter build apk --release
    if ($LASTEXITCODE -ne 0) {
        throw "flutter build apk failed with exit code $LASTEXITCODE"
    }
    $apkSource = Join-Path $mobileDir 'build\app\outputs\flutter-apk\app-release.apk'
    if (Test-Path $apkSource) {
        $dest = Copy-Item -Path $apkSource -Destination (Join-Path $outputDir 'Pconnect.apk') -Force -PassThru
        Write-Host "Build complete! APK saved to: $($dest.FullName)"

        $webpageReleases = Join-Path $PSScriptRoot '..\webpage\public\releases'
        if (Test-Path $webpageReleases) {
            Copy-Item -Path $apkSource -Destination (Join-Path $webpageReleases 'Pconnect.apk') -Force
            Write-Host "Copied APK to: $webpageReleases"
        }
        $websiteReleases = Join-Path $PSScriptRoot '..\website\public\releases'
        if (Test-Path $websiteReleases) {
            Copy-Item -Path $apkSource -Destination (Join-Path $websiteReleases 'Pconnect.apk') -Force
            Write-Host "Copied APK to: $websiteReleases"
        }
    } else {
        throw "Build finished, but app-release.apk was not found at $apkSource."
    }
} finally {
    Pop-Location
}

