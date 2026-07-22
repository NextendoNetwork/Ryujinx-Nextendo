$ErrorActionPreference = 'Stop'

# Change this value to update the published application version for all platforms.
$PublishVersion = '1.6.5'
$Configuration = 'Release'
$ProjectPath = 'src/Ryujinx/Ryujinx.csproj'
$SelfContained = 'true'
$ArchiveOutputRoot = 'publish/release-zips'

$Targets = @(
    @{ Name = 'windows-x64'; Rid = 'win-x64'; Output = "publish/Ryujinx-Nextendo-$PublishVersion-win-x64" }
    @{ Name = 'windows-arm64'; Rid = 'win-arm64'; Output = "publish/Ryujinx-Nextendo-$PublishVersion-win-arm64" }
    @{ Name = 'linux-x64'; Rid = 'linux-x64'; Output = "publish/Ryujinx-Nextendo-$PublishVersion-linux-x64" }
    @{ Name = 'linux-arm64'; Rid = 'linux-arm64'; Output = "publish/Ryujinx-Nextendo-$PublishVersion-linux-arm64" }
    @{ Name = 'macos-arm64'; Rid = 'osx-arm64'; Output = "publish/Ryujinx-Nextendo-$PublishVersion-osx-arm64" }
    @{ Name = 'osx-x64'; Rid = 'osx-x64'; Output = "publish/Ryujinx-Nextendo-$PublishVersion-osx-x64" }
)

New-Item -ItemType Directory -Path $ArchiveOutputRoot -Force | Out-Null

foreach ($target in $Targets) {
    Write-Host "Publishing $($target.Name) ($($target.Rid))..." -ForegroundColor Cyan

    dotnet publish $ProjectPath `
        -c $Configuration `
        -r $target.Rid `
        --self-contained $SelfContained `
        -p:Version=$PublishVersion `
        -p:DebugSymbols=false `
        -p:DebugType=None `
        -o $target.Output

    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed for $($target.Name) ($($target.Rid))."
    }

    $portablePath = Join-Path $target.Output 'portable'
    New-Item -ItemType Directory -Path $portablePath -Force | Out-Null

    $releaseName = Split-Path $target.Output -Leaf
    $zipPath = Join-Path $ArchiveOutputRoot ("$releaseName.zip")
    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }

    # Zip only the contents of the release folder, not the folder itself.
    Compress-Archive -Path (Join-Path $target.Output '*') -DestinationPath $zipPath
    Write-Host "Created archive: $zipPath" -ForegroundColor Yellow
}

Write-Host "All platform publishes completed successfully with version $PublishVersion." -ForegroundColor Green
