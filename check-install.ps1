Write-Host "=== Scheduled Tasks ==="
try {
    $task = Get-ScheduledTask -TaskName 'PEI Site Service' -ErrorAction Stop
    Write-Host "Task Found: $($task.TaskName) - State: $($task.State)"
} catch {
    Write-Host "TASK NOT FOUND"
}

Write-Host ""
Write-Host "=== HKCU Run Registry ==="
$props = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -ErrorAction SilentlyContinue
if ($props) {
    $props.PSObject.Properties | ForEach-Object {
        if ($_.Name -notlike 'PS*') {
            Write-Host "$($_.Name) = $($_.Value)"
        }
    }
} else {
    Write-Host "No HKCU Run entries"
}

Write-Host ""
Write-Host "=== HKLM Run Registry ==="
$props2 = Get-ItemProperty 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run' -ErrorAction SilentlyContinue
if ($props2) {
    $props2.PSObject.Properties | ForEach-Object {
        if ($_.Name -notlike 'PS*') {
            Write-Host "$($_.Name) = $($_.Value)"
        }
    }
} else {
    Write-Host "No HKLM Run entries"
}

Write-Host ""
Write-Host "=== PEI Site Service process ==="
Get-Process -Name 'pei-site-service' -ErrorAction SilentlyContinue | Format-Table Name,Id,StartTime

Write-Host "=== Installed PEI App location ==="
$installPath = Get-ItemProperty 'HKLM:\Software\WOW6432Node\PEI Data Systems\PEI Site App' -ErrorAction SilentlyContinue
if ($installPath) {
    Write-Host "InstallPath: $($installPath.InstallPath)"
} else {
    Write-Host "No install registry found (WOW6432Node)"
    $installPath2 = Get-ItemProperty 'HKLM:\Software\PEI Data Systems\PEI Site App' -ErrorAction SilentlyContinue
    if ($installPath2) {
        Write-Host "InstallPath (64-bit): $($installPath2.InstallPath)"
    } else {
        Write-Host "No install registry found"
    }
}
