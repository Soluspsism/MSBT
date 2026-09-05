param(
    [string] $ProjectRoot = ".",
    [string] $ManifestPath = "pluginmaster.json",
    [string] $OutputDirectory = "release-assets"
)

$ErrorActionPreference = "Stop"

$projectFiles = @(Get-ChildItem -Path $ProjectRoot -Filter "*.csproj" -File -Recurse |
    Where-Object { $_.FullName -notmatch "[\\/](?:bin|obj)[\\/]" })

$versionedProjects = @(
    foreach ($projectFile in $projectFiles) {
        [xml] $project = Get-Content -LiteralPath $projectFile.FullName -Raw
        $version = @($project.Project.PropertyGroup.Version) |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Select-Object -First 1

        if ($version) {
            $projectFile
        }
    }
)

if ($versionedProjects.Count -ne 1) {
    throw "Expected exactly one .csproj with a <Version> value, but found $($versionedProjects.Count)."
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json -NoEnumerate
if ($manifest -isnot [System.Array]) {
    throw "pluginmaster.json must contain a JSON array."
}

if ($manifest.Count -ne 1) {
    throw "Expected pluginmaster.json to contain exactly one plugin entry, but found $($manifest.Count)."
}

$plugin = $manifest[0]
$pluginName = [string] $plugin.InternalName
$downloadUrl = [string] $plugin.DownloadLinkInstall
if ([string]::IsNullOrWhiteSpace($pluginName) -or [string]::IsNullOrWhiteSpace($downloadUrl)) {
    throw "The plugin entry must contain InternalName and DownloadLinkInstall values."
}

$assetName = [System.IO.Path]::GetFileName(([Uri] $downloadUrl).AbsolutePath)
if ([string]::IsNullOrWhiteSpace($assetName) -or
    [System.IO.Path]::GetExtension($assetName) -ne ".zip") {
    throw "DownloadLinkInstall must end with a .zip filename."
}

$projectFile = $versionedProjects[0]
& dotnet build $projectFile.FullName --configuration Release
if ($LASTEXITCODE -ne 0) {
    throw "The release build failed with exit code $LASTEXITCODE."
}

$packagePath = Join-Path $projectFile.Directory.FullName "bin/Release/$pluginName/latest.zip"
if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
    throw "The build did not create the expected package: $packagePath"
}

$null = New-Item -Path $OutputDirectory -ItemType Directory -Force
$assetPath = Join-Path $OutputDirectory $assetName
Copy-Item -LiteralPath $packagePath -Destination $assetPath -Force

Write-Host "Created release asset $assetPath."
