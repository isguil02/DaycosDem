param (
    [Parameter(Position=0)]
    [string]$Command = "help",
    [Parameter(Position=1)]
    [string]$CarrierName,
    [Parameter(Position=2)]
    [string]$Arg2
)

$ApiBase = "http://localhost:5111"

switch ($Command.ToLower()) {
    "submit" {
        if (-not $CarrierName) {
            Write-Host "Usage: .\carrier.ps1 submit 'Allied Van Lines' 3500.00" -ForegroundColor Yellow
            return
        }
        $amount = if ($Arg2) { [decimal]$Arg2 } else { 2500.00 }
        $invNum = "INV-$($CarrierName.Substring(0,3).ToUpper())-$((Get-Random -Minimum 1000 -Maximum 9999))"
        $body = @{
            invoiceNumber = $invNum
            carrierName = $CarrierName
            weightLbs = 7200
            amount = $amount
        } | ConvertTo-Json
        
                Write-Host "[SUBMIT] Submitting invoice for $CarrierName..." -ForegroundColor Cyan
        $res = Invoke-RestMethod -Uri "$ApiBase/invoices" -Method Post -Body $body -ContentType "application/json"
        Write-Host "[OK] Created invoice #$($res.invoiceNumber) for `$$($res.amount)" -ForegroundColor Green
    }
    "status" {
        if (-not $CarrierName) {
            Write-Host "Usage: .\carrier.ps1 status 'Allied Van Lines'" -ForegroundColor Yellow
            return
        }
        Write-Host "🔍 Fetching invoices for: $CarrierName..." -ForegroundColor Cyan
        $invoices = Invoke-RestMethod -Uri "$ApiBase/invoices" -Method Get
        $mine = $invoices | Where-Object { $_.carrierName -eq $CarrierName }
        $mine | Format-Table id, invoiceNumber, createdAt, weightLbs, amount, isDuplicate
    }
    Default {
        Write-Host "=== Daycos Carrier Portal CLI ===" -ForegroundColor Cyan
        Write-Host "Usage: .\carrier.ps1 [command] [CarrierName] [Options]"
        Write-Host "  submit [Carrier] [Amount] - Submit a new freight invoice"
        Write-Host "  status [Carrier]          - Check active invoices for your carrier"
    }
}