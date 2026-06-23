$ErrorActionPreference = 'Stop'

Write-Host "Building Android Mobile App (Flutter)..."

$env:GRADLE_USER_HOME = "d:\Projects\Pconnect\.gradle-user-home"

if (Test-Path "C:\Program Files\Android\Android Studio\jbr") {
    $env:JAVA_HOME = "C:\Program Files\Android\Android Studio\jbr"
    $env:Path = "$env:JAVA_HOME\bin;$env:Path"
}

Push-Location (Join-Path $PSScriptRoot '..\mobile\pconnect_mobile')
try {
    flutter pub get
    flutter build apk --release
    Write-Host "Build complete."
} finally {
    Pop-Location
}
