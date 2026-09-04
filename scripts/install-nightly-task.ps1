<#
.SYNOPSIS
  Đăng ký Windows Task Scheduler chạy DhcbTools.BatchRunner mỗi đêm (mục 1.5).

.EXAMPLE
  .\install-nightly-task.ps1 -Job "D:\DHCB\jobs\nightly.json" -RunnerExe "D:\DHCB\bin\DhcbTools.BatchRunner.exe" -LogDir "\\server\dhcb\logs" -Time 23:00

.NOTES
  Task chạy dưới tài khoản hiện tại (phải có license Revit/AutoCAD và đã đăng nhập Autodesk ít nhất một lần).
  Mã thoát 1/2 của runner → Task Scheduler ghi Last Run Result ≠ 0, dùng để cảnh báo.
#>
param(
    [Parameter(Mandatory = $true)] [string] $Job,
    [Parameter(Mandatory = $true)] [string] $RunnerExe,
    [string] $LogDir = "$env:USERPROFILE\Documents\DHCB\logs",
    [string] $Time = "23:00",
    [int] $MaxMinutes = 480,
    [string] $TaskName = "DHCB Tools - Batch đêm",
    [switch] $Analyze
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Job)) { throw "Không tìm thấy file job: $Job" }
if (-not (Test-Path $RunnerExe)) { throw "Không tìm thấy runner: $RunnerExe" }

# Không dùng tên $args: đó là biến tự động của PowerShell (tham số không khai báo), ghi đè nó là lỗi ngầm.
$runnerArgs = "--job `"$Job`" --log-dir `"$LogDir`" --max-minutes $MaxMinutes"
if ($Analyze) { $runnerArgs += " --analyze" }

$action = New-ScheduledTaskAction -Execute $RunnerExe -Argument $runnerArgs -WorkingDirectory (Split-Path $RunnerExe)
$trigger = New-ScheduledTaskTrigger -Daily -At $Time
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes ($MaxMinutes + 30)) -StartWhenAvailable -WakeToRun
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited

$task = Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force
if (-not $task) { throw "Register-ScheduledTask không trả về task — chưa đăng ký được '$TaskName'." }

Write-Host "Đã đăng ký task '$TaskName' chạy $Time hàng ngày."
Write-Host "  $RunnerExe $runnerArgs"
Write-Host "Chạy thử ngay: Start-ScheduledTask -TaskName '$TaskName'"
