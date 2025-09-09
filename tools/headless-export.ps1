param(
    [string]$OutZip = "$PSScriptRoot/../artifacts/out/diagnostics.zip",
    [string]$ExePath,
    [int]$TimeoutSec = 120
)

# Robustly locate the application executable if not provided
if (-not $ExePath) {
    $candidates = @()
    $candidates += Join-Path $PSScriptRoot "../artifacts/publish/desktop/KitchenInventory.Desktop.exe"
    $candidates += Join-Path $PSScriptRoot "../artifacts/publish/desktop-scd-singlefile-win-x64/KitchenInventory.Desktop.exe"
    $candidates += "$Env:LOCALAPPDATA/Programs/KitchenInventory/KitchenInventory.Desktop.exe"
    $candidates += "$Env:ProgramFiles/KitchenInventory/KitchenInventory.Desktop.exe"
    $candidates += "$Env:ProgramFiles(x86)/KitchenInventory/KitchenInventory.Desktop.exe"

    foreach ($p in $candidates) {
        $full = [System.IO.Path]::GetFullPath($p)
        if (Test-Path $full) { $ExePath = $full; break }
    }
}

if (-not (Test-Path $ExePath)) {
    Write-Error "Executable not found. Provide -ExePath. Checked common locations; last tried: $ExePath"
    exit 1
}

$zipOut = [System.IO.Path]::GetFullPath($OutZip)
$zipDir = Split-Path -Parent $zipOut
New-Item -ItemType Directory -Force -Path $zipDir | Out-Null

# Ensure headless mode
$Env:INVENTORY_HEADLESS = "1"
$Env:CI = "true"

# Prepare args (use = form so the app picks up the provided path)
$exportArgs = @("--export-diagnostics=$zipOut", "--headless")

Write-Host "Running: $ExePath $($exportArgs -join ' ')"

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $ExePath
# Build a robust quoted argument string compatible with Windows CreateProcess
$quotedArgs = $exportArgs | ForEach-Object {
    if ($_ -match '[\s\"`]') { '"' + ($_.ToString().Replace('"','\"')) + '"' } else { $_ }
}
$psi.Arguments = ($quotedArgs -join ' ')
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true

$p = New-Object System.Diagnostics.Process
$p.StartInfo = $psi
$p.Start() | Out-Null

$stdoutTask = $p.StandardOutput.ReadToEndAsync()
$stderrTask = $p.StandardError.ReadToEndAsync()

# Poll for the zip to become available and valid, without requiring the process to exit
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSec)
$zipVerified = $false
$lastZipError = $null
while ([DateTime]::UtcNow -lt $deadline) {
    if (Test-Path $zipOut) {
        try {
            Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue | Out-Null
            $zip = [System.IO.Compression.ZipFile]::OpenRead($zipOut)
            $entries = $zip.Entries | Select-Object -ExpandProperty FullName
            $zip.Dispose()
            if ($entries.Count -gt 0) {
                $zipVerified = $true
                break
            } else {
                $lastZipError = "Zip has 0 entries"
            }
        }
        catch {
            $lastZipError = $_
        }
    }

    if ($p.HasExited) { break }
    Start-Sleep -Seconds 2
}

# Attempt to read logs (only once the process has exited or we choose to end it)
function Read-Logs {
    param($proc, $outTask, $errTask)
    $o = ""; $e = ""
    try { if ($outTask) { $o = $outTask.GetAwaiter().GetResult() } } catch {}
    try { if ($errTask) { $e = $errTask.GetAwaiter().GetResult() } } catch {}
    Write-Host "--- STDOUT ---"
    if ($o) { Write-Host $o }
    Write-Host "--- STDERR ---"
    if ($e) { Write-Host $e }
}

if ($zipVerified) {
    # Give the app a moment to exit on its own
    if (-not $p.WaitForExit(5000)) {
        try { $null = $p.CloseMainWindow() } catch {}
        Start-Sleep -Seconds 2
    }
    if (-not $p.HasExited) {
        try { $p.Kill() } catch {}
        $null = $p.WaitForExit(2000)
    }

    Read-Logs -proc $p -outTask $stdoutTask -errTask $stderrTask

    # Re-open the zip for final listing
    try {
        Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue | Out-Null
        $zip = [System.IO.Compression.ZipFile]::OpenRead($zipOut)
        $entries = $zip.Entries | Select-Object -ExpandProperty FullName
        $zip.Dispose()
        Write-Host "Diagnostics export ZIP exists at: $zipOut"
        Write-Host "ZIP entries (count=$($entries.Count)):"
        $entries | ForEach-Object { Write-Host " - $_" }
        Write-Host "VERDICT: SUCCESS"
        exit 0
    }
    catch {
        Write-Error "Failed to open/inspect zip after success condition: $_"
        Write-Host "VERDICT: FAIL"
        exit 5
    }
}
else {
    # Either timed out or process exited without producing a valid zip
    $remainingMs = [int][Math]::Max(0, ($deadline - [DateTime]::UtcNow).TotalMilliseconds)
    if ($remainingMs -gt 0 -and -not $p.HasExited) {
        $null = $p.WaitForExit($remainingMs)
    }
    if (-not $p.HasExited) {
        try { $p.Kill() } catch {}
        $null = $p.WaitForExit(2000)
    }

    Read-Logs -proc $p -outTask $stdoutTask -errTask $stderrTask

    if (-not (Test-Path $zipOut)) {
        Write-Error "Export zip not found: $zipOut"
        Write-Host "VERDICT: FAIL"
        exit 2
    }
    else {
        Write-Error "Export zip was present but invalid: $lastZipError"
        Write-Host "VERDICT: FAIL"
        exit 4
    }
}