# 두 AutoCAD 프로세스(Core Console)가 같은 '고친 템플릿'을 같은 캐시 폴더로 동시에 분석하는 시험.
# 결과: verification\concurrency-verification.txt (SUCCESS: n concurrency checks.)
param([string]$AutoCADPath = 'C:\Program Files\Autodesk\AutoCAD 2020', [int]$Processes = 2)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$verifyDir = (Join-Path $root 'bin\Verify').Replace('\', '/')
$work = Join-Path $root ('work\concurrency-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$install = Join-Path $work 'install'; $cache = Join-Path $work 'cache'
New-Item -ItemType Directory -Force -Path $install, $cache | Out-Null
$lines = New-Object System.Collections.Generic.List[string]
$script:fail = 0; $script:count = 0
function Check([bool]$ok, [string]$label) { $script:count++; if ($ok) { $lines.Add('PASS: ' + $label) } else { $script:fail++; $lines.Add('FAIL: ' + $label) } }

function Start-Console([string]$command, [string]$id) {
    $scr = Join-Path $work ($command + '-' + $id + '.scr')
    $body = @('(setq ahj-old-tp (getvar "TRUSTEDPATHS"))', ('(setvar "TRUSTEDPATHS" "' + $verifyDir + '")'), '_.NETLOAD', ('"' + $verifyDir + '/StormLegend.Verify.dll"'), $command, '(setvar "TRUSTEDPATHS" ahj-old-tp)', '_.QUIT', '_Y')
    [IO.File]::WriteAllLines($scr, $body, [Text.Encoding]::GetEncoding(949))
    $env:LEGEND_PROJECT_ROOT = $root; $env:LEGEND_CONC_INSTALL = $install; $env:LEGEND_CONC_CACHE = $cache; $env:LEGEND_CONC_ID = $id
    $iso = Join-Path $work ('iso-' + $id)
    return Start-Process -FilePath (Join-Path $AutoCADPath 'accoreconsole.exe') -ArgumentList ('/s "' + $scr + '" /isolate LegendConc' + $id + ' "' + $iso + '"') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $work ('stdout-' + $id + '.log'))
}

try {
    $setup = Start-Console 'LEGENDCONCSETUP' 'setup'
    if (!$setup.WaitForExit(120000)) { Stop-Process -Id $setup.Id -Force; throw 'setup timeout' }
    $setupResult = Get-Content -LiteralPath (Join-Path $install 'setup.txt') -Raw -Encoding UTF8
    Check ($setupResult -like 'PASS*') ('준비: 고친 템플릿 만들기 ' + $setupResult.Trim())

    $procs = @(); for ($i = 1; $i -le $Processes; $i++) { $procs += Start-Console 'LEGENDCONCURRENT' ([string]$i) }
    $t = Get-Date
    while (@(1..$Processes | Where-Object { Test-Path -LiteralPath (Join-Path $install ('ready-' + $_)) }).Count -lt $Processes -and ((Get-Date) - $t).TotalSeconds -lt 240) { Start-Sleep -Milliseconds 100 }
    Check (@(1..$Processes | Where-Object { Test-Path -LiteralPath (Join-Path $install ('ready-' + $_)) }).Count -eq $Processes) ('프로세스 ' + $Processes + '개 모두 준비')
    [IO.File]::WriteAllText((Join-Path $install 'go'), '')   # 동시에 출발
    foreach ($p in $procs) { if (!$p.WaitForExit(300000)) { Stop-Process -Id $p.Id -Force } }

    $results = @(1..$Processes | ForEach-Object {
        $file = Join-Path $install ('result-' + $_ + '.txt')
        if (Test-Path -LiteralPath $file) { ,(Get-Content -LiteralPath $file -Encoding UTF8) } else { ,@('FAIL: no result') }
    })
    for ($i = 0; $i -lt $Processes; $i++) { Check ($results[$i][0] -like 'PASS*') ('프로세스 ' + ($i + 1) + ': ' + ($results[$i] -join ' ')) }
    $dirs = @($results | ForEach-Object { ($_ | Where-Object { $_ -like 'dir=*' }) -replace '^dir=', '' } | Sort-Object -Unique)
    Check ($dirs.Count -eq 1 -and $dirs[0] -like 'a*-*') ('모두 같은 분석본 사용: ' + ($dirs -join ','))
    Check (@($results | Where-Object { ($_ -join ' ') -like '*source=Changed*' -and ($_ -join ' ') -like '*paving=10*' }).Count -eq $Processes) '모두 고친 템플릿(포장 10행)으로 열림'
    $left = @(Get-ChildItem -LiteralPath $cache -Directory | Where-Object { $_.Name -like '*.invalid-*' -or $_.Name -like '*.tmp-*' -or $_.Name -like '*.copy-*' })
    Check ($left.Count -eq 0) ('정상 캐시를 격리·삭제하지 않음(남은 임시/격리 폴더 ' + $left.Count + ')')
    $caches = @(Get-ChildItem -LiteralPath $cache -Directory | Where-Object { $_.Name -ne 'history' })
    Check ($caches.Count -eq 1) ('분석본 폴더는 하나: ' + (($caches | ForEach-Object { $_.Name }) -join ','))
    $starts = @($results | ForEach-Object { ($_ | Where-Object { $_ -like 'started=*' }) -replace '^started=', '' })
    $lines.Add('INFO: 시작 시각 ' + ($starts -join ' / ') + ' · 소요(ms) ' + ((@($results | ForEach-Object { ($_ | Where-Object { $_ -like 'ms=*' }) -replace '^ms=', '' })) -join ' / '))
}
catch { $script:fail++; $lines.Add('FAIL: ' + $_.Exception.Message) }
finally {
    if ($script:fail -ne 0) { foreach ($log in Get-ChildItem -LiteralPath $work -Filter 'stdout-*.log' -ErrorAction SilentlyContinue) { try { $lines.Add('LOG ' + $log.Name + ': ' + (([Text.Encoding]::Unicode.GetString([IO.File]::ReadAllBytes($log.FullName)) -replace '\s+', ' ') | ForEach-Object { $_.Substring([Math]::Max(0, $_.Length - 600)) })) } catch { } } }
    try { Remove-Item -LiteralPath $work -Recurse -Force } catch { }
}
$lines.Add($(if ($script:fail -eq 0) { 'SUCCESS: ' + $script:count + ' concurrency checks.' } else { 'FAILED: ' + $script:fail + ' of ' + $script:count }))
$out = Join-Path $root 'verification\concurrency-verification.txt'
[IO.File]::WriteAllLines($out, $lines, (New-Object Text.UTF8Encoding($true)))
$lines
if ($script:fail -ne 0) { exit 1 }
