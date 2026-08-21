[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = Join-Path $projectRoot 'artifacts'
$publishRoot = Join-Path $artifactsRoot 'publish\win-x64'
$releaseRoot = Join-Path $artifactsRoot 'release'
$project = Join-Path $projectRoot 'src\TuckPane\TuckPane.csproj'
$installer = Join-Path $projectRoot 'installer\TuckPane.iss'

function Reset-BuildDirectory([string]$Path) {
    $resolvedArtifacts = [IO.Path]::GetFullPath($artifactsRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $resolvedPath = [IO.Path]::GetFullPath($Path)
    if (-not $resolvedPath.StartsWith($resolvedArtifacts, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear a path outside artifacts: $resolvedPath"
    }
    if (Test-Path -LiteralPath $resolvedPath) { Remove-Item -LiteralPath $resolvedPath -Recurse -Force }
    New-Item -ItemType Directory -Path $resolvedPath -Force | Out-Null
}

Reset-BuildDirectory $publishRoot
Reset-BuildDirectory $releaseRoot

dotnet restore $project --locked-mode -p:Platform=x64
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }

dotnet publish $project `
    -c Release `
    --no-restore `
    -p:Platform=x64 `
    -p:RuntimeIdentifier=win-x64 `
    -p:SelfContained=true `
    -p:WindowsAppSDKSelfContained=true `
    -p:DebugSymbols=false `
    -p:DebugType=None `
    -o $publishRoot
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

$requiredFiles = @('TuckPane.exe', 'TuckPane.dll', 'TuckPane.pri', 'hostfxr.dll', 'Microsoft.WindowsAppRuntime.dll')
foreach ($requiredFile in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishRoot $requiredFile))) {
        throw "Publish is incomplete. Missing: $requiredFile"
    }
}

$fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $publishRoot 'TuckPane.exe'))
if ($fileVersion.FileVersion -ne "$Version.0" -or $fileVersion.ProductName -ne 'TuckPane') {
    throw "Unexpected executable metadata: $($fileVersion.FileVersion), $($fileVersion.ProductName)"
}

Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $publishRoot
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination $publishRoot
Copy-Item -LiteralPath (Join-Path $projectRoot 'licenses') -Destination $publishRoot -Recurse

$privateArtifacts = @(Get-ChildItem -LiteralPath $publishRoot -Recurse -File | Where-Object {
    $_.Extension -eq '.pdb' -or $_.Name -in @('state.json', 'state.json.bak') -or $_.Extension -eq '.log'
})
if ($privateArtifacts.Count -gt 0) {
    throw "Private/debug files entered the package: $($privateArtifacts.FullName -join ', ')"
}

$portablePath = Join-Path $releaseRoot "TuckPane-$Version-win-x64-portable.zip"
Compress-Archive -Path (Join-Path $publishRoot '*') -DestinationPath $portablePath -CompressionLevel Optimal

$isccCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
$iscc = $isccCandidates | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 is required to build the offline installer.' }

& $iscc "/DMyAppVersion=$Version" "/DPublishDir=$publishRoot" "/DOutputDir=$releaseRoot" $installer
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }

$setupPath = Join-Path $releaseRoot "TuckPane-$Version-win-x64-setup.exe"
if (-not (Test-Path -LiteralPath $setupPath)) { throw "Installer was not created: $setupPath" }

$hashPath = Join-Path $releaseRoot 'SHA256SUMS.txt'
$hashLines = @($setupPath, $portablePath) | ForEach-Object {
    $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $(Split-Path $_ -Leaf)"
}
[IO.File]::WriteAllLines($hashPath, $hashLines, [Text.UTF8Encoding]::new($false))

Get-ChildItem -LiteralPath $releaseRoot -File | Select-Object Name, Length, LastWriteTime
