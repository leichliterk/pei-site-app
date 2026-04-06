param(
    [ValidateSet("Staging", "Production")]
    [string]$Environment = "Staging",

    [Parameter(Mandatory)]
    [string]$Version
)

$ErrorActionPreference = "Stop"
$wix  = "C:\Program Files (x86)\WiX Toolset v3.14\bin"
$root = $PSScriptRoot

Write-Host ""
Write-Host "=== PEI Site App Build ===" -ForegroundColor Cyan
Write-Host "  Environment : $Environment"
Write-Host "  Version     : $Version"
Write-Host ""

# ---------------------------------------------------------------------------
# 1. Wipe bin+obj and publish both projects (guarantees a full recompile)
# ---------------------------------------------------------------------------
$appDir     = Join-Path $root "service-dotnet\PeiSiteApp"
$serviceDir = Join-Path $root "service-dotnet\PeiSiteService"

Write-Host "--- Cleaning PeiSiteApp ---" -ForegroundColor Yellow
Remove-Item -Recurse -Force (Join-Path $appDir "bin") -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force (Join-Path $appDir "obj") -ErrorAction SilentlyContinue

Write-Host "--- Publishing PeiSiteApp ---" -ForegroundColor Yellow
& dotnet publish $appDir -c Release -r win-x64 --self-contained true -p:BuildEnvironment=$Environment
if ($LASTEXITCODE -ne 0) { throw "PeiSiteApp publish failed" }

Write-Host ""
Write-Host "--- Cleaning PeiSiteService ---" -ForegroundColor Yellow
Remove-Item -Recurse -Force (Join-Path $serviceDir "bin") -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force (Join-Path $serviceDir "obj") -ErrorAction SilentlyContinue

Write-Host "--- Publishing PeiSiteService ---" -ForegroundColor Yellow
& dotnet publish $serviceDir -c Release -r win-x64 --self-contained true -p:BuildEnvironment=$Environment
if ($LASTEXITCODE -ne 0) { throw "PeiSiteService publish failed" }

# ---------------------------------------------------------------------------
# 2. Select output names and folder
# ---------------------------------------------------------------------------
if ($Environment -eq "Production") {
    $msiFolder   = "$root/release/msi"
    $msiName     = "PEI Site App $Version.msi"
    $exeName     = "PEI Site App Setup $Version.exe"
    $productName = "PEI Site App"
} else {
    $msiFolder   = "$root/release/msi-staging"
    $msiName     = "PEI Site App - Staging $Version.msi"
    $exeName     = "PEI Site App - Staging Setup $Version.exe"
    $productName = "PEI Site App - Staging"
}

# ---------------------------------------------------------------------------
# 3. Patch version numbers into WXS files
#    (WiX candle cannot expand preprocessor variables in Bundle/@Version)
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "--- Patching WXS versions to $Version ---" -ForegroundColor Yellow

$msiWxs    = "$msiFolder/PEI Site App.wxs"
$bundleWxs = "$msiFolder/Bundle.wxs"

# Update Product/@Version and Comments in MSI WXS
(Get-Content $msiWxs -Raw) `
    -replace '(?<=<Product[^>]+Version=")[^"]+', $Version `
    -replace '(?<=Comments=")[^"]+', "Installs $productName v$Version" |
    Set-Content $msiWxs -NoNewline

# Update Bundle/@Version in Bundle WXS
(Get-Content $bundleWxs -Raw) `
    -replace '(?<=<Bundle[^>]+Version=")[^"]+', $Version |
    Set-Content $bundleWxs -NoNewline

# ---------------------------------------------------------------------------
# 4. Build MSI
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "--- Building MSI ---" -ForegroundColor Yellow
Push-Location $msiFolder

& "$wix\candle.exe" -ext WixFirewallExtension -ext WixUtilExtension `
    "PEI Site App.wxs" -out "PEI Site App.wixobj"
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "candle (MSI) failed" }

& "$wix\light.exe" -ext WixFirewallExtension -ext WixUIExtension -ext WixUtilExtension `
    "PEI Site App.wixobj" -out $msiName
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "light (MSI) failed" }

# ---------------------------------------------------------------------------
# 5. Build Burn bootstrapper
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "--- Building Bootstrapper ---" -ForegroundColor Yellow

& "$wix\candle.exe" -ext WixBalExtension `
    "-dMsiFile=$msiName" `
    "Bundle.wxs" -out "Bundle.wixobj"
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "candle (Bundle) failed" }

& "$wix\light.exe" -ext WixBalExtension `
    "Bundle.wixobj" -out $exeName
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "light (Bundle) failed" }

Pop-Location

# ---------------------------------------------------------------------------
# Done
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Build complete ===" -ForegroundColor Green
Write-Host "  Installer: $msiFolder\$exeName"
Write-Host ""
