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

# Validate inputs before clearing only the explicitly selected release directory.
foreach ($relative in @('About', 'Defs', 'Languages', 'LoadFolders.xml', '1.6\Assemblies\ColonyPromises.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $SourceRoot $relative))) { throw "Missing runtime input: $relative" }
}
[xml]$loadFolders = [IO.File]::ReadAllText((Join-Path $SourceRoot 'LoadFolders.xml'))
if (($loadFolders.loadFolders.'v1.6'.li -join ',') -ne '/,1.6') { throw 'Unexpected RimWorld 1.6 load folders.' }
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
    $assemblies = Join-Path $ReleaseRoot '1.6\Assemblies'
    New-Item -ItemType Directory -Path $assemblies -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $SourceRoot '1.6\Assemblies\ColonyPromises.dll') -Destination $assemblies
} finally {
    # Restore metadata even if copying runtime files fails.
    New-Item -ItemType Directory -Path (Join-Path $ReleaseRoot 'About') -Force | Out-Null
    if ($null -ne $idBytes) { [IO.File]::WriteAllBytes((Join-Path $ReleaseRoot 'About\PublishedFileId.txt'), $idBytes) }
    if ($null -ne $previewBytes) { [IO.File]::WriteAllBytes($previewPath, $previewBytes) }
}
Write-Output "Prepared runtime package: $ReleaseRoot"
