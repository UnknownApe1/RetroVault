param([string]$Patch)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (-not (Test-Path ".git")) {
    throw "This folder is not a Git working copy. Run this updater from C:\RomManager."
}

if (Get-Process -Name "RomManager" -ErrorAction SilentlyContinue) {
    throw "Close ROM Manager before installing an update."
}

$changes = @(git status --porcelain --untracked-files=all)
if ($LASTEXITCODE -ne 0) { throw "Could not inspect the Git working copy." }
if ($changes.Count -gt 0) {
    throw "The source folder has uncommitted changes. Commit or discard them before updating.`n$($changes -join "`n")"
}

if ([string]::IsNullOrWhiteSpace($Patch)) {
    $candidate = Get-ChildItem -Path ".\Patches" -Filter "RomManager-*.patch" -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($null -eq $candidate) {
        throw "No update was found. Put the downloaded .patch file in C:\RomManager\Patches and run Update.cmd again."
    }
    $Patch = $candidate.FullName
}

$resolvedPatch = (Resolve-Path $Patch).Path
Write-Host "Checking $resolvedPatch"
git apply --check $resolvedPatch
if ($LASTEXITCODE -ne 0) { throw "This patch does not match the installed version." }

git apply $resolvedPatch
if ($LASTEXITCODE -ne 0) { throw "Git could not apply the update." }

try {
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\Build.ps1"
    if ($LASTEXITCODE -ne 0) { throw "The updated source did not build successfully." }

    git add -A
    if ($LASTEXITCODE -ne 0) { throw "The update built, but Git could not stage it." }
    git commit -m "Apply $([System.IO.Path]::GetFileNameWithoutExtension($resolvedPatch))"
    if ($LASTEXITCODE -ne 0) { throw "The update built, but Git could not record the new baseline." }
}
catch {
    Write-Warning "The patch remains applied so its build error can be inspected. It was not committed."
    throw
}

Write-Host "Update installed and verified successfully." -ForegroundColor Green
Write-Host "Start the app from .\RomManager.App\bin\Release\net10.0-windows\RomManager.exe"
