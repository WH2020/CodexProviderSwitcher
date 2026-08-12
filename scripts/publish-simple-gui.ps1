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
$artifactsRoot = Join-Path $repoRoot 'artifacts'
$sentinelName = '.codex-provider-switcher-publish-root'
$sentinelContent = "codex-provider-switcher-simple-publish-root-v1`n"

function Test-SamePath([string]$Left, [string]$Right) {
    return $Left.Equals($Right, [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-NoReparsePoints([string]$Path) {
    if (-not $Path.StartsWith($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside the repository: $Path"
    }
    $relative = $Path.Substring($repoRoot.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $current = $repoRoot
    foreach ($segment in $relative -split '[\\/]') {
        if ($segment -eq '.' -or [string]::IsNullOrWhiteSpace($segment)) { continue }
        $current = Join-Path $current $segment
        if (Test-Path -LiteralPath $current) {
            $attributes = (Get-Item -LiteralPath $current -Force).Attributes
            if (($attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Publish output path contains a reparse point: $current"
            }
        }
    }
}

$artifactsRoot = [System.IO.Path]::GetFullPath($artifactsRoot)
$artifactsPrefix = $artifactsRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if ((Test-SamePath $outputDir $repoRoot) -or (Test-SamePath $outputDir $artifactsRoot) -or
    (-not $outputDir.StartsWith($artifactsPrefix, [System.StringComparison]::OrdinalIgnoreCase))) {
    throw "Publish output must be a non-root leaf below repository artifacts: $outputDir"
}

Assert-NoReparsePoints $artifactsRoot
Assert-NoReparsePoints $outputDir

if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "Simple GUI project is missing: $project"
}

Write-Host "Resolved publish output: $outputDir"
if (-not (Test-Path -LiteralPath $artifactsRoot)) {
    New-Item -ItemType Directory -Path $artifactsRoot | Out-Null
}

if (-not (Test-Path -LiteralPath $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}

$sentinel = Join-Path $outputDir $sentinelName
$children = Get-ChildItem -LiteralPath $outputDir -Force
if ($children.Count -gt 0) {
    if (-not (Test-Path -LiteralPath $sentinel -PathType Leaf) -or
        (Get-Content -LiteralPath $sentinel -Raw) -cne $sentinelContent) {
        throw "Refusing to clean non-empty output without the expected ownership sentinel: $outputDir"
    }
    Write-Host "Cleaning owned publish output contents: $outputDir"
    foreach ($child in $children) {
        if ($child.Name -ne $sentinelName) {
            Write-Host "Removing owned publish output item: $($child.FullName)"
            Remove-Item -LiteralPath $child.FullName -Recurse -Force
        }
    }
} elseif (-not (Test-Path -LiteralPath $sentinel)) {
    Set-Content -LiteralPath $sentinel -Value $sentinelContent -NoNewline
}

if ((Get-Content -LiteralPath $sentinel -Raw) -cne $sentinelContent) {
    Set-Content -LiteralPath $sentinel -Value $sentinelContent -NoNewline
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
