param (
    [Parameter(Position=0)]
    [string]$Command = "help",
    [Parameter(Position=1)]
    [string]$Arg1
)

$ApiBase = "http://localhost:5111"

switch ($Command.ToLower()) {
    "backup" {
        Write-Host "[BACKUP] Creating 4-table snapshot in backups/..." -ForegroundColor Cyan
        $res = Invoke-RestMethod -Uri "$ApiBase/system/backup" -Method Post
        Write-Host "[OK] Backup created: $($res.backupFolder) ($($res.invoices) invoices, $($res.carriers) carriers)" -ForegroundColor Green
    }
    "nuke" {
        Write-Host "[WARN] SIMULATING DISASTER: Deleting all invoices without WHERE clause..." -ForegroundColor Red
        $res = Invoke-RestMethod -Uri "$ApiBase/system/nuke" -Method Post
        Write-Host "[WIPED] Table has 0 invoices! Refresh dashboard to see $0 spend." -ForegroundColor Yellow
    }
    "restore" {
        Write-Host "[RESTORE] Restoring ledger from newest backup folder..." -ForegroundColor Cyan
        $res = Invoke-RestMethod -Uri "$ApiBase/system/restore-backup" -Method Post
        Write-Host "[OK] $($res.message)" -ForegroundColor Green
    }
    "seed" {
        $count = if ($Arg1) { [int]$Arg1 } else { 50 }
        Write-Host "[SEED] Seeding $count synthetic invoices into PostgreSQL..." -ForegroundColor Cyan
        $body = @{ count = $count } | ConvertTo-Json
        $res = Invoke-RestMethod -Uri "$ApiBase/invoices/seed" -Method Post -Body $body -ContentType "application/json"
        Write-Host "[OK] Seeded $count invoices." -ForegroundColor Green
    }
    Default {
        Write-Host "=== Daycos Audit Manager CLI ===" -ForegroundColor Yellow
        Write-Host "Usage: .\cli\Manager.ps1 [command]"
        Write-Host "  backup        - Snapshot all 4 tables to backups/ folder"
        Write-Host "  nuke          - Delete all invoices (Simulate accidental wipe)"
        Write-Host "  restore       - Restore invoices from newest backup folder"
        Write-Host "  seed [count]  - Seed synthetic invoices (e.g. .\cli\Manager.ps1 seed 100)"
    }
}