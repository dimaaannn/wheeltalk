<#
.SYNOPSIS
Снимает обход стенда: все варианты панели в характерных точках сценария.

.DESCRIPTION
Снимает через adb, а не средствами приложения: снимок изнутри на эмуляторе отдавал кадр,
отставший от экрана, и полтора десятка снимков разных панелей превращались в пару одинаковых
картинок. Обход по шагам ведёт WheelTalk.Lab.Droid/Scenarios/ShotWalk.cs.

Порядок шагов приложение выкладывает в order.txt при входе в обход, поэтому скрипт не зависит ни
от координат кнопок, ни от того, сколько в сценарии меток.

.EXAMPLE
В стенде: выбрать сценарий, нажать ⤓. Затем:
    tools/lab-shots.ps1 -Scenario mten3-calm-ride
#>
param(
    [Parameter(Mandatory)][string]$Scenario,
    [string]$Serial = "emulator-5554",
    [string]$Out = "shots"
)

$ErrorActionPreference = "Stop"
$adb = "C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe"
$remote = "/sdcard/Android/data/com.wheeltalk.lab.droid/files/shots/$Scenario"

$order = & $adb -s $Serial shell cat "$remote/order.txt" | ForEach-Object { $_.Trim() } | Where-Object { $_ }
if (-not $order) { throw "Нет $remote/order.txt — стенд в обход не входил (кнопка ⤓)." }

# Касание в середину экрана двигает обход на шаг: хром в это время спрятан, попасть больше не во что.
$size = (& $adb -s $Serial shell wm size) -replace '.*:\s*', ''
$width, $height = $size -split 'x'
$tapX = [int]$width / 2
$tapY = [int]$height / 2

$folder = Join-Path $Out $Scenario
New-Item -ItemType Directory -Force $folder | Out-Null

foreach ($name in $order) {
    Start-Sleep -Milliseconds 600
    & $adb -s $Serial exec-out screencap -p > (Join-Path $folder "$name.png")
    & $adb -s $Serial shell input tap $tapX $tapY
}

Write-Host "Снято $($order.Count) в $folder"
