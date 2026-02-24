param(
    [ValidateSet("Debug", "Release")]
    [string]$Config = "Debug"
)

$root = $PSScriptRoot
$source = Join-Path $root "TiltDetector\bin\$Config"

if ($Config -eq "Debug") {
    $destRoot = "C:\QuantowerDev"
} else {
    $destRoot = "C:\Quantower"
}

$dest = "$destRoot\Settings\Scripts\Strategies\TiltDetector"

if ([string]::IsNullOrWhiteSpace($dest)) {
    Write-Error "Destination path is empty. Aborting."
    exit 1
}

if ($dest -notmatch "Quantower.*TiltDetector$") {
    Write-Error "Destination path doesn't look right: $dest. Aborting."
    exit 1
}

if (-not (Test-Path $source)) {
    Write-Error "Source path doesn't exist: $source. Did you build first?"
    exit 1
}

if (Test-Path $dest) {
    Write-Host "Cleaning $dest"
    Remove-Item "$dest\*" -Force -Recurse
} else {
    Write-Host "Destination does not exist, creating: $dest"
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
}

$files = @("*.dll", "*.pdb", "*.deps.json")

foreach ($pattern in $files) {
    $items = Get-ChildItem -Path $source -Filter $pattern
    foreach ($item in $items) {
        Copy-Item $item.FullName -Destination $dest -Force
        Write-Host "Deployed: $($item.Name)"
    }
}

Write-Host "[$Config] Deploy complete to $dest"
