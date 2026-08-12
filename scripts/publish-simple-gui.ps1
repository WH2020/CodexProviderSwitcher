param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$Output = 'artifacts\simple-win-x64',
    [string]$DotnetPath = 'dotnet'
)

$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Resolve-Path (Join-Path $PSScriptRoot '..')).Path)
$project = Join-Path $repoRoot 'desktop\CodexProviderSync.SimpleApp\CodexProviderSync.SimpleApp.csproj'
$outputDir = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Output))
$separator = [System.IO.Path]::DirectorySeparatorChar
$repoPrefix = $repoRoot.TrimEnd($separator) + $separator

if ($outputDir.Equals($repoRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
    -not $outputDir.StartsWith($repoPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Publish output must be a subdirectory of the repository and not the repository root: $outputDir"
}

if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "Simple GUI project is missing: $project"
}

Write-Host "Resolved publish output: $outputDir"
if (Test-Path -LiteralPath $outputDir) {
    Write-Host "Removing publish output: $outputDir"
    Remove-Item -LiteralPath $outputDir -Recurse -Force
}

& $DotnetPath publish $project `
    --runtime $Runtime `
    -c $Configuration `
    --self-contained true `
    -o $outputDir `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:EnableCompressionInSingleFile=true `
    /p:DebugType=None `
    /p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    throw "Simple GUI dotnet publish failed with exit code $LASTEXITCODE"
}

$executable = Join-Path $outputDir 'CodexProviderSwitcher.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Simple GUI executable was not produced: $executable"
}

Write-Host "Published simple GUI executable: $([System.IO.Path]::GetFullPath($executable))"
