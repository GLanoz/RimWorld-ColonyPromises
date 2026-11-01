param(
    [string]$SourceRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$ReleaseRoot
)

$ErrorActionPreference = 'Stop'
$SourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path.TrimEnd('\')
if (-not $ReleaseRoot) { $ReleaseRoot = Join-Path (Split-Path $SourceRoot -Parent) 'ColonyPromises-release' }
$ReleaseRoot = [IO.Path]::GetFullPath($ReleaseRoot).TrimEnd('\')
if ($ReleaseRoot -eq $SourceRoot -or $ReleaseRoot.StartsWith($SourceRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $SourceRoot.StartsWith($ReleaseRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Source and release directories must not contain each other.'
}
foreach ($root in @($SourceRoot, $ReleaseRoot)) {
    if ((Test-Path -LiteralPath $root) -and ((Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Refusing a linked directory: $root"
    }
}

# Check all IDs before changing the package. Prefer the release bytes when IDs agree.
$idFiles = @()
foreach ($root in @($ReleaseRoot, $SourceRoot)) {
    if (Test-Path -LiteralPath $root) {
        $idFiles += @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter 'PublishedFileId.txt')
    }
}
$ids = @($idFiles | ForEach-Object { [IO.File]::ReadAllText($_.FullName).Trim() } | Select-Object -Unique)
if ($ids.Count -gt 1) {
    throw ('Conflicting Workshop IDs; package left unchanged: ' + ($idFiles.FullName -join '; '))
}
$idBytes = $null
if ($idFiles.Count) { $idBytes = [IO.File]::ReadAllBytes($idFiles[0].FullName) }
$previewBytes = $null
$previewPath = Join-Path $ReleaseRoot 'About\preview.png'
if (Test-Path -LiteralPath $previewPath) { $previewBytes = [IO.File]::ReadAllBytes($previewPath) }

# Validate both supported outputs before clearing only the selected release directory.
$versions = @('1.6', '1.7')
foreach ($relative in @('About', 'Defs', 'Languages', 'LoadFolders.xml', 'Textures/UI/MainButtons/Promises.png') + @($versions | ForEach-Object { "$_\Assemblies\ColonyPromises.dll" })) {
    if (-not (Test-Path -LiteralPath (Join-Path $SourceRoot $relative))) { throw "Missing runtime input: $relative" }
}
[xml]$loadFolders = [IO.File]::ReadAllText((Join-Path $SourceRoot 'LoadFolders.xml'))
[xml]$about = [IO.File]::ReadAllText((Join-Path $SourceRoot 'About/About.xml'))
foreach ($version in $versions) {
    if (($loadFolders.loadFolders.SelectSingleNode("v$version").li -join ',') -ne "/,$version") { throw "Unexpected RimWorld $version load folders." }
    if ($version -notin $about.ModMetaData.supportedVersions.li) { throw "RimWorld $version is missing from About.xml." }
}
try {
    if (Test-Path -LiteralPath $ReleaseRoot) {
        foreach ($entry in Get-ChildItem -LiteralPath $ReleaseRoot -Force) {
            $target = [IO.Path]::GetFullPath($entry.FullName)
            if (-not $target.StartsWith($ReleaseRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Cleanup escaped release directory.' }
            Remove-Item -LiteralPath $target -Recurse -Force
        }
    } else { New-Item -ItemType Directory -Path $ReleaseRoot | Out-Null }
    foreach ($folder in @('About', 'Defs', 'Languages')) {
        Copy-Item -LiteralPath (Join-Path $SourceRoot $folder) -Destination $ReleaseRoot -Recurse
    }
    Copy-Item -LiteralPath (Join-Path $SourceRoot 'LoadFolders.xml') -Destination $ReleaseRoot
    $textureFolder = Join-Path $ReleaseRoot 'Textures/UI/MainButtons'
    New-Item -ItemType Directory -Path $textureFolder -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $SourceRoot 'Textures/UI/MainButtons/Promises.png') -Destination $textureFolder
    foreach ($version in $versions) {
        $assemblies = Join-Path $ReleaseRoot "$version\Assemblies"
        New-Item -ItemType Directory -Path $assemblies -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $SourceRoot "$version\Assemblies\ColonyPromises.dll") -Destination $assemblies
        if ((Get-FileHash -LiteralPath (Join-Path $assemblies 'ColonyPromises.dll')).Hash -ne
            (Get-FileHash -LiteralPath (Join-Path $SourceRoot "$version\Assemblies\ColonyPromises.dll")).Hash) {
            throw "Packaged RimWorld $version DLL does not match its build output."
        }
    }
} finally {
    # Restore metadata even if copying runtime files fails.
    New-Item -ItemType Directory -Path (Join-Path $ReleaseRoot 'About') -Force | Out-Null
    if ($null -ne $idBytes) { [IO.File]::WriteAllBytes((Join-Path $ReleaseRoot 'About\PublishedFileId.txt'), $idBytes) }
    if ($null -ne $previewBytes) { [IO.File]::WriteAllBytes($previewPath, $previewBytes) }
}
Write-Output "Prepared runtime package: $ReleaseRoot"
