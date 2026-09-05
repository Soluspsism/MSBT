param(
    [string] $ProjectRoot = ".",
    [string] $ManifestPath = "pluginmaster.json",
    [DateTimeOffset] $ReleasedAt = [DateTimeOffset]::UtcNow
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
            [PSCustomObject]@{
                File = $projectFile.FullName
                Version = $version.Trim()
            }
        }
    }
)

if ($versionedProjects.Count -ne 1) {
    throw "Expected exactly one .csproj with a <Version> value, but found $($versionedProjects.Count)."
}

$version = $versionedProjects[0].Version
if ($version.Contains("`$(")) {
    throw "The project <Version> must be a literal value, not an MSBuild expression."
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json -NoEnumerate
if ($manifest -isnot [System.Array]) {
    throw "pluginmaster.json must contain a JSON array."
}

if ($manifest.Count -eq 0) {
    throw "pluginmaster.json must contain at least one plugin entry."
}

$lastUpdate = $ReleasedAt.ToUnixTimeSeconds()
foreach ($plugin in $manifest) {
    $plugin.AssemblyVersion = $version
    $plugin.LastUpdate = $lastUpdate
}

$json = ConvertTo-Json -InputObject $manifest -Depth 100
$absoluteManifestPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ManifestPath)
[System.IO.File]::WriteAllText(
    $absoluteManifestPath,
    "$json`n",
    [System.Text.UTF8Encoding]::new($false)
)

Write-Host "Updated $ManifestPath to version $version."
