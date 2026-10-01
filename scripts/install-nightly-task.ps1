<#
.SYNOPSIS
  Đăng ký Windows Task Scheduler chạy DhcbTools.BatchRunner mỗi đêm (mục 1.5).

.EXAMPLE
  .\install-nightly-task.ps1 -Job "D:\DHCB\jobs\nightly.json" -RunnerExe "D:\DHCB\bin\DhcbTools.BatchRunner.exe" -LogDir "\\server\dhcb\logs" -Time 23:00

.NOTES
  Task chạy dưới tài khoản hiện tại (phải có license Revit/AutoCAD và đã đăng nhập Autodesk ít nhất một lần).
  Mã thoát 1/2 của runner → Task Scheduler ghi Last Run Result ≠ 0, dùng để cảnh báo. Runner vì thế phải là
  action CUỐI của task (bước dọn don-ket-qua.ps1 chạy trước nó).
  Chạy được bằng Windows PowerShell 5.1 lẫn PowerShell 7.
#>
param(
    [Parameter(Mandatory = $true)] [string] $Job,
    [Parameter(Mandatory = $true)] [string] $RunnerExe,
    [string] $LogDir = "$env:USERPROFILE\Documents\DHCB\logs",
    [string] $Time = "23:00",
    [int] $MaxMinutes = 480,
    [switch] $NoPrune,
    [string] $TaskName = "DHCB Tools - Batch đêm",
    [switch] $Analyze
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Job)) { throw "Không tìm thấy file job: $Job" }
if (-not (Test-Path $RunnerExe)) { throw "Không tìm thấy runner: $RunnerExe" }

# Đường dẫn có dấu nháy kép sẽ phá chuỗi tham số và chèn được cờ lạ vào task — từ chối sớm.
foreach ($p in @($Job, $LogDir, $RunnerExe)) {
    if ($p -match '"') { throw "Đường dẫn không được chứa dấu nháy kép: $p" }
}
# Không dùng tên $args: đó là biến tự động của PowerShell (tham số không khai báo), ghi đè nó là lỗi ngầm.
$runnerArgs = "--job `"$Job`" --log-dir `"$LogDir`" --max-minutes $MaxMinutes"
if ($Analyze) { $runnerArgs += " --analyze" }

$action = New-ScheduledTaskAction -Execute $RunnerExe -Argument $runnerArgs -WorkingDirectory (Split-Path $RunnerExe)
# Action dọn DHCB-test-results + journal Revit (§61) đặt TRƯỚC runner, runner là action CUỐI: Task Scheduler chạy
# các action tuần tự bất kể mã thoát, và "Last Run Result" là mã của action chạy sau cùng. Bản cũ dọn SAU runner
# nên mã 1/2 của batch (cách duy nhất để cảnh báo, xem .NOTES) bị mã của bước dọn che mất — còn máy không có
# thư mục DHCB-test-results thì task báo 2 mỗi đêm dù batch xanh.
$prune = Join-Path $PSScriptRoot 'don-ket-qua.ps1'
$actions = @()
if (-not $NoPrune -and (Test-Path $prune)) {
    # Không dùng ?. / ?? (chỉ có ở PowerShell 7): Windows PowerShell 5.1 — bản có sẵn trên mọi máy — báo lỗi cú
    # pháp cho CẢ script và task không bao giờ được đăng ký.
    $pwshCommand = Get-Command pwsh -ErrorAction SilentlyContinue
    $pwsh = if ($pwshCommand) { $pwshCommand.Source } else { (Get-Command powershell).Source }
    $actions += New-ScheduledTaskAction -Execute $pwsh -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$prune`" -Apply" -WorkingDirectory (Split-Path $prune)
}
$actions += $action
$trigger = New-ScheduledTaskTrigger -Daily -At $Time
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes ($MaxMinutes + 30)) -StartWhenAvailable -WakeToRun
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited

$task = Register-ScheduledTask -TaskName $TaskName -Action $actions -Trigger $trigger -Settings $settings -Principal $principal -Force
if (-not $task) { throw "Register-ScheduledTask không trả về task — chưa đăng ký được '$TaskName'." }

Write-Host "Đã đăng ký task '$TaskName' chạy $Time hàng ngày."
Write-Host "  $RunnerExe $runnerArgs"
Write-Host "Chạy thử ngay: Start-ScheduledTask -TaskName '$TaskName'"
