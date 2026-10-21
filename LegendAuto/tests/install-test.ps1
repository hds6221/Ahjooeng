# 설치 프로그램 시험: 가짜 사용자 폴더(APPDATA)에 설치해 실제 설치 위치는 건드리지 않습니다.
# 사용: powershell -File tests\install-test.ps1 -Package <설치 묶음 폴더(install.ps1·Ahjooeng.bundle 포함)>
param([Parameter(Mandatory = $true)][string]$Package)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$script:failures = 0; $script:checks = 0
function Check([bool]$ok, [string]$label) { $script:checks++; if ($ok) { Write-Output ('PASS: ' + $label) } else { $script:failures++; Write-Output ('FAIL: ' + $label) } }
function Hash([string]$path) { if (Test-Path -LiteralPath $path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash } else { '' } }

$install = Join-Path $Package 'install.ps1'
$fake = Join-Path $env:TEMP ('ahjooeng-install-test-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$plugins = Join-Path $fake 'Autodesk\ApplicationPlugins'
$target = Join-Path $plugins 'Ahjooeng.bundle'
$tpl = Join-Path $target 'Contents\LegendAuto\Template\범례템플릿.dwg'
$dll = Join-Path $target 'Contents\LegendAuto\StormLegend.dll'
$realAppData = $env:APPDATA

function Run-Install([string]$fault = '') {
    $env:AHJOOENG_INSTALL_TEST_FAULT = $fault
    & powershell -NoProfile -ExecutionPolicy Bypass -File $install -Quiet *> $null
    $code = $LASTEXITCODE
    $env:AHJOOENG_INSTALL_TEST_FAULT = ''
    return $code
}

try {
    New-Item -ItemType Directory -Force -Path $fake | Out-Null
    $env:APPDATA = $fake
    $shippedTemplate = Hash (Join-Path $Package 'Ahjooeng.bundle\Contents\LegendAuto\Template\범례템플릿.dwg')

    # 1) 처음 설치
    Check ((Run-Install) -eq 0) '처음 설치 성공'
    Check ((Hash $dll) -eq (Hash (Join-Path $Package 'Ahjooeng.bundle\Contents\LegendAuto\StormLegend.dll'))) '설치된 DLL = 묶음 DLL'
    Check ((Hash $tpl) -eq $shippedTemplate) '처음 설치는 배포 템플릿'
    Check (@(Get-ChildItem -LiteralPath $plugins -Directory | Where-Object { $_.Name -like 'Ahjooeng.stage-*' }).Count -eq 0) '임시 설치 폴더가 남지 않음'

    # 2) 사용자가 템플릿을 고치고 다른 파일도 둔 뒤 재설치 → 보존
    [IO.File]::AppendAllText($tpl, 'user-edit')
    $userTemplate = Hash $tpl
    $extra = Join-Path (Split-Path $tpl) '내 백업.dwg'; [IO.File]::WriteAllText($extra, 'backup')
    Check ((Run-Install) -eq 0) '재설치 성공'
    Check ((Hash $tpl) -eq $userTemplate) '재설치: 사용자 템플릿 보존'
    Check ((Hash (Join-Path (Split-Path $tpl) '범례템플릿(새 배포본).dwg')) -eq $shippedTemplate) '재설치: 새 배포 템플릿은 (새 배포본)으로'
    Check ((Test-Path -LiteralPath $extra) -and [IO.File]::ReadAllText($extra) -eq 'backup') '재설치: Template 폴더의 다른 사용자 파일도 옮김'
    $backups = @(Get-ChildItem -LiteralPath $plugins -Directory -Filter 'Ahjooeng.previous-*')
    Check ($backups.Count -eq 1) ('재설치: 이전 설치 백업 1개 (' + $backups.Count + ')')

    # 3) 이전 설치를 옮긴 직후 실패 → 이전 설치 복원, 백업 보존
    $before = Get-ChildItem -LiteralPath $target -Recurse -File | ForEach-Object { $_.FullName.Substring($target.Length) + '=' + (Hash $_.FullName) }
    Check ((Run-Install 'after-backup') -ne 0) '실패 주입: 설치가 실패로 끝남'
    $after = Get-ChildItem -LiteralPath $target -Recurse -File | ForEach-Object { $_.FullName.Substring($target.Length) + '=' + (Hash $_.FullName) }
    Check (($before -join '|') -eq ($after -join '|')) '실패 주입: 이전 설치가 파일 하나까지 그대로 복원'
    Check (@(Get-ChildItem -LiteralPath $plugins -Directory -Filter 'Ahjooeng.previous-*').Count -eq 1) '실패 주입: 기존 정상 백업은 그대로'

    # 4) 재시도 → 성공, 사용자 템플릿 계속 보존
    Check ((Run-Install) -eq 0) '재시도 성공'
    Check ((Hash $tpl) -eq $userTemplate) '재시도: 사용자 템플릿 보존'

    # 5) AutoCAD가 DLL을 쓰는 중(파일 잠금) → 설치 거부, 설치본 그대로
    $snapshot = Hash $dll
    $lock = [IO.File]::Open($dll, 'Open', 'Read', 'Read')
    try { Check ((Run-Install) -ne 0) 'DLL 사용 중: 설치 거부' } finally { $lock.Close() }
    Check ((Hash $dll) -eq $snapshot -and (Hash $tpl) -eq $userTemplate) 'DLL 사용 중: 설치본·템플릿 그대로'

    # 6) 여러 번 설치해도 백업은 최근 3개만
    for ($i = 0; $i -lt 4; $i++) { Start-Sleep -Milliseconds 1100; $null = Run-Install }
    $count = @(Get-ChildItem -LiteralPath $plugins -Directory -Filter 'Ahjooeng.previous-*').Count
    Check ($count -eq 3) ('백업은 최근 3개만 (' + $count + ')')
    Check ((Hash $tpl) -eq $userTemplate) '여러 번 설치 후에도 사용자 템플릿 보존'

    # 7) 예전 배포 템플릿(고치지 않음)은 사용자 수정본으로 오인하지 않음
    Copy-Item -LiteralPath (Join-Path $Package 'Ahjooeng.bundle\Contents\LegendAuto\Template\범례템플릿.dwg') -Destination $tpl -Force
    Remove-Item -LiteralPath (Join-Path (Split-Path $tpl) '범례템플릿(새 배포본).dwg') -Force
    Check ((Run-Install) -eq 0 -and -not (Test-Path -LiteralPath (Join-Path (Split-Path $tpl) '범례템플릿(새 배포본).dwg'))) '배포본 그대로인 템플릿은 새 배포본으로 교체(보존 대상 아님)'
}
finally {
    $env:APPDATA = $realAppData
    $env:AHJOOENG_INSTALL_TEST_FAULT = ''
    try { Remove-Item -LiteralPath $fake -Recurse -Force } catch { }
}
if ($script:failures -eq 0) { Write-Output ('SUCCESS: ' + $script:checks + ' install checks.'); exit 0 }
Write-Output ('FAILED: ' + $script:failures + ' of ' + $script:checks); exit 1
