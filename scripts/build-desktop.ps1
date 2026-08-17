param(
  [ValidateSet('Debug','Release')]
  [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

Write-Host "Building Windows agent ($Configuration)..."

$sdks = & dotnet --list-sdks 2>$null
if (-not $sdks) {
  throw "No .NET SDK found. Install .NET SDK 8.x from https://dotnet.microsoft.com/download"
}

$desktopDir = Join-Path $PSScriptRoot '..\desktop\Pconnect.Agent'
$outputDir = Join-Path $PSScriptRoot '..\releases'
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}

Push-Location $desktopDir
try {
  dotnet publish -c $Configuration -r win-x64 /p:PublishSingleFile=true /p:SelfContained=true
  if ($LASTEXITCODE -ne 0) {
      throw "dotnet publish failed with exit code $LASTEXITCODE"
  }
  $exeSource = Join-Path $desktopDir "bin\$Configuration\net8.0-windows10.0.26100.0\win-x64\publish\Pconnect.Agent.exe"
  if (Test-Path $exeSource) {
      $dest = Copy-Item -Path $exeSource -Destination (Join-Path $outputDir 'Pconnect.Agent.exe') -Force -PassThru
      Write-Host "Publish complete! EXE saved to: $($dest.FullName)"
      
      $webpageReleases = Join-Path $PSScriptRoot '..\webpage\public\releases'
      if (Test-Path $webpageReleases) {
          Copy-Item -Path $exeSource -Destination (Join-Path $webpageReleases 'Pconnect.Agent.exe') -Force
          Write-Host "Copied EXE to: $webpageReleases"
      }
      $websiteReleases = Join-Path $PSScriptRoot '..\website\public\releases'
      if (Test-Path $websiteReleases) {
          Copy-Item -Path $exeSource -Destination (Join-Path $websiteReleases 'Pconnect.Agent.exe') -Force
          Write-Host "Copied EXE to: $websiteReleases"
      }
  } else {
      throw "Published file not found at $exeSource"
  }
} finally {
  Pop-Location
}

