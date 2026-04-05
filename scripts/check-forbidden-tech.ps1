$forbiddenPatterns = @(
    "UseWPF",
    "Microsoft.NET.Sdk.WindowsDesktop",
    "WebView",
    "Electron",
    "Tauri",
    "Blazor",
    ".html",
    ".css",
    ".js",
    ".tsx",
    ".jsx"
)

$rootDir = Get-Location
$srcDir = Join-Path $rootDir "src"
$failed = $false

Write-Host "Checking for forbidden technologies..."

foreach ($pattern in $forbiddenPatterns) {
    if ($pattern.StartsWith(".")) {
        $matches = Get-ChildItem -Path $srcDir -Filter "*$pattern" -Recurse -ErrorAction SilentlyContinue 
    } else {
        $matches = Select-String -Path "$srcDir\**\*.*" -Pattern $pattern -Exclude "*.md", "*.png", "*.jpg", "*.ico", "*.axaml", "*.xaml" -ErrorAction SilentlyContinue
    }

    if ($matches) {
        Write-Error "Forbidden pattern '$pattern' found in:"
        $matches | ForEach-Object { Write-Error "  $($_.FullName)" }
        $failed = $true
    }
}

# Special check for Avalonia in Core
$coreProject = Get-ChildItem -Path $srcDir -Filter "ImageCaptionSearch.Core.csproj" -Recurse
if ($coreProject) {
    $coreContent = Get-Content $coreProject.FullName
    if ($coreContent -match "Avalonia") {
        Write-Error "Avalonia reference found in ImageCaptionSearch.Core project file."
        $failed = $true
    }
}

if ($failed) {
    Write-Host "Policy check FAILED." -ForegroundColor Red
    exit 1
} else {
    Write-Host "Policy check PASSED." -ForegroundColor Green
    exit 0
}
