# Ahjooeng 설치: AutoCAD 자동 로드 폴더에 플러그인을 넣습니다. 한 번 설치하면 AutoCAD를 켤 때마다 자동으로 불러옵니다.
# 설치 위치: %APPDATA%\Autodesk\ApplicationPlugins\Ahjooeng.bundle  (이 사용자 계정에만 설치, 관리자 권한 불필요)
param([switch]$Quiet)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $here 'Ahjooeng.bundle'
$pluginRoot = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins'
$target = Join-Path $pluginRoot 'Ahjooeng.bundle'
$templateRel = 'Contents\LegendAuto\Template\범례템플릿.dwg'
$templateDirRel = 'Contents\LegendAuto\Template'
$keepBackups = 3
# 시험용 실패 주입(tests/install-test.ps1 전용). 평소에는 비어 있습니다.
$fault = $env:AHJOOENG_INSTALL_TEST_FAULT

function Assert-InstallChild([string]$candidate) {
    $resolvedRoot = [IO.Path]::GetFullPath($pluginRoot).TrimEnd('\') + '\'
    $resolvedTarget = [IO.Path]::GetFullPath($candidate)
    if (!$resolvedTarget.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw "설치 폴더 밖 경로는 변경할 수 없습니다: $resolvedTarget" }
}

function Say($text, $color = 'Gray') { Write-Host $text -ForegroundColor $color }
function Pause-End { if (!$Quiet) { Write-Host ''; Read-Host '끝내려면 Enter를 누르세요' | Out-Null } }

try {
    if (!(Test-Path -LiteralPath (Join-Path $source 'PackageContents.xml'))) { throw '설치 파일이 온전하지 않습니다. 압축을 모두 푼 폴더에서 실행하세요.' }
    Say '=== Ahjooeng 설치 (AutoCAD 2020 범례 생성) ===' 'Cyan'

    # 이미 설치되어 있고 AutoCAD가 그 DLL을 쓰고 있으면 덮어쓸 수 없습니다.
    $installedDll = Join-Path $target 'Contents\LegendAuto\StormLegend.dll'
    if (Test-Path -LiteralPath $installedDll) {
        try { $stream = [IO.File]::Open($installedDll, 'Open', 'ReadWrite', 'None'); $stream.Close() }
        catch { throw 'AutoCAD가 지금 이 프로그램을 쓰고 있어 바꿀 수 없습니다. AutoCAD를 모두 닫은 뒤 다시 설치하세요.' }
    }
    if (Get-Process acad -ErrorAction SilentlyContinue) { Say '※ AutoCAD가 켜져 있습니다. 설치는 계속하고, AutoCAD를 다시 켜면 적용됩니다.' 'Yellow' }

    # 새 설치를 먼저 완성한 뒤 폴더 이름으로 교체합니다. 정상 백업은 재시도해도 지우지 않습니다.
    New-Item -ItemType Directory -Force -Path $pluginRoot | Out-Null
    $stage = Join-Path $pluginRoot ('Ahjooeng.stage-' + [Guid]::NewGuid().ToString('N'))
    $backup = Join-Path $pluginRoot ('Ahjooeng.previous-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N'))
    Assert-InstallChild $target
    Assert-InstallChild $stage
    Assert-InstallChild $backup
    $movedOld = $false
    $published = $false
    try {
        foreach ($required in @('PackageContents.xml', 'Contents\LegendAuto\StormLegend.dll', 'Contents\LegendAuto\StormLegend.Core.dll', $templateRel, 'Contents\LegendAuto\Template\catalog-index.json', 'Contents\LegendAuto\Template\preview-catalog.json')) {
            if (!(Test-Path -LiteralPath (Join-Path $source $required) -PathType Leaf)) { throw "설치 파일 누락: $required" }
        }
        Copy-Item -LiteralPath $source -Destination $stage -Recurse
        foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File) {
            $relative = $file.FullName.Substring($source.Length + 1)
            $copied = Join-Path $stage $relative
            if (!(Test-Path -LiteralPath $copied -PathType Leaf) -or (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $copied -Algorithm SHA256).Hash) { throw "설치 복사 검증 실패: $relative" }
        }
        $oldTemplate = Join-Path $target $templateRel
        $newTemplate = Join-Path $stage $templateRel
        $keptTemplate = $false
        if (Test-Path -LiteralPath $oldTemplate) {
            $oldHash = (Get-FileHash -LiteralPath $oldTemplate -Algorithm SHA256).Hash
            # 지금까지 배포한 원본 템플릿 해시(모든 버전). 여기에 없는 템플릿만 '사용자가 고친 것'으로 보존합니다.
            $shipped = @(Get-Content -LiteralPath (Join-Path $here 'shipped-templates.txt') -ErrorAction SilentlyContinue | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ })
            if ($oldHash -ne (Get-FileHash -LiteralPath $newTemplate -Algorithm SHA256).Hash -and ($shipped -notcontains $oldHash.ToLowerInvariant())) {
                Copy-Item -LiteralPath $newTemplate -Destination (Join-Path (Split-Path $newTemplate) '범례템플릿(새 배포본).dwg')
                Copy-Item -LiteralPath $oldTemplate -Destination $newTemplate -Force
                if ((Get-FileHash -LiteralPath $newTemplate -Algorithm SHA256).Hash -ne $oldHash) { throw '사용자 템플릿 보존 검증 실패' }
                $keptTemplate = $true
            }
        }
        # 이전 Template 폴더에 사용자가 둔 다른 파일(백업 DWG·.bak 등)은 새 설치에 없는 이름만 옮겨 둡니다(덮어쓰지 않음).
        $oldTemplateDir = Join-Path $target $templateDirRel
        $carried = 0
        if (Test-Path -LiteralPath $oldTemplateDir) {
            foreach ($file in Get-ChildItem -LiteralPath $oldTemplateDir -Recurse -File) {
                $relative = $file.FullName.Substring($oldTemplateDir.Length + 1)
                $destination = Join-Path (Join-Path $stage $templateDirRel) $relative
                if (Test-Path -LiteralPath $destination) { continue }
                New-Item -ItemType Directory -Force -Path (Split-Path $destination) | Out-Null
                Copy-Item -LiteralPath $file.FullName -Destination $destination
                if ((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash) { throw "사용자 파일 보존 검증 실패: $relative" }
                $carried++
            }
        }
        Get-ChildItem -LiteralPath $stage -Recurse -File | Unblock-File
        if (Test-Path -LiteralPath $target) { Move-Item -LiteralPath $target -Destination $backup; $movedOld = $true }
        if ($fault -eq 'after-backup') { throw '시험용 실패(이전 설치를 옮긴 뒤)' }
        Move-Item -LiteralPath $stage -Destination $target
        $published = $true
        if ($movedOld) { Say ('이전 설치를 보관했습니다: ' + $backup) }
        if ($keptTemplate) { Say '고쳐 쓰던 템플릿을 보존했습니다. 새 배포 템플릿은 같은 폴더의 범례템플릿(새 배포본).dwg입니다.' 'Yellow' }
        if ($carried -gt 0) { Say ('Template 폴더의 다른 파일 ' + $carried + '개도 새 설치로 옮겼습니다.') }
        # 정상 설치가 끝난 뒤에만 오래된 백업을 정리합니다(최근 3개 유지).
        $old = @(Get-ChildItem -LiteralPath $pluginRoot -Directory -Filter 'Ahjooeng.previous-*' | Sort-Object Name -Descending | Select-Object -Skip $keepBackups)
        foreach ($dir in $old) {
            Assert-InstallChild $dir.FullName
            try { Remove-Item -LiteralPath $dir.FullName -Recurse -Force } catch { Say ('오래된 백업 정리 실패(직접 지워도 됩니다): ' + $dir.FullName) 'Yellow' }
        }
    }
    catch {
        $installError = $_
        if ($movedOld -and !$published) {
            try {
                if (Test-Path -LiteralPath $target) { throw '설치 경로가 다른 프로세스에 의해 생성되었습니다.' }
                Move-Item -LiteralPath $backup -Destination $target
                Say '설치 실패로 이전 설치를 복원했습니다.' 'Yellow'
            }
            catch { Say ('자동 복원 실패: ' + $_.Exception.Message + ' / 정상 이전 설치 보관 위치: ' + $backup) 'Red' }
        }
        throw $installError
    }
    finally {
        if (Test-Path -LiteralPath $stage) {
            Assert-InstallChild $stage
            try { Remove-Item -LiteralPath $stage -Recurse -Force } catch { Say ('임시 설치 폴더 정리 실패: ' + $stage) 'Yellow' }
        }
    }

    # 예전처럼 시작하기 세트(APPLOAD)에 StormLegend.dll을 넣어 두었으면 두 번 불러오게 되므로 알려줍니다.
    $startup = Get-ChildItem 'HKCU:\Software\Autodesk\AutoCAD\R23.1' -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.PSChildName -eq 'Startup' } | ForEach-Object { $k = $_; $k.Property | ForEach-Object { $k.GetValue($_) } } |
        Where-Object { $_ -is [string] -and $_ -like '*StormLegend.dll*' }
    if ($startup) { Say ('※ 시작하기 세트(APPLOAD)에 이전 범례 DLL이 등록되어 있습니다: ' + ($startup -join ', ') + '  → APPLOAD > 시작하기 세트에서 빼세요(이제 자동으로 불러옵니다).') 'Yellow' }

    Say ''
    Say '설치 완료!' 'Green'
    Say '  1. AutoCAD를 켜면 메뉴 막대에 [Ahjooeng] 메뉴가 생깁니다.'
    Say '  2. 메뉴 Ahjooeng > 범례 생성 (LGD)를 누르거나 명령줄에 LGD를 입력하세요. 사용법은 Ahjooeng > 도움말을 누르세요.'
    Say '  3. 이제 NETLOAD는 하지 않아도 됩니다.'
    Say ('  설치 위치: ' + $target)
    Pause-End
    exit 0
}
catch {
    Say ('설치하지 못했습니다: ' + $_.Exception.Message) 'Red'
    Pause-End
    exit 1
}
