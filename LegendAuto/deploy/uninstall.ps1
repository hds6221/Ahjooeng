# Ahjooeng 제거: 자동 로드 폴더의 Ahjooeng.bundle을 지웁니다. 사람마다 저장한 프리셋·설정(%LOCALAPPDATA%\StormLegend)은 남겨 둡니다.
param([switch]$Quiet)
$ErrorActionPreference = 'Stop'
$target = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\Ahjooeng.bundle'
function Say($text, $color = 'Gray') { Write-Host $text -ForegroundColor $color }
function Pause-End { if (!$Quiet) { Write-Host ''; Read-Host '끝내려면 Enter를 누르세요' | Out-Null } }
try {
    Say '=== Ahjooeng 제거 ===' 'Cyan'
    if (!(Test-Path -LiteralPath $target)) { Say '설치되어 있지 않습니다.'; Pause-End; exit 0 }
    if (Get-Process acad -ErrorAction SilentlyContinue) { throw 'AutoCAD를 모두 닫은 뒤 다시 실행하세요.' }
    if (!$Quiet) {
        $answer = Read-Host 'Ahjooeng(범례 생성)을 제거할까요? 고친 범례 템플릿도 함께 지워집니다. (Y/N)'
        if ($answer -notmatch '^[Yy]') { Say '제거하지 않았습니다.'; Pause-End; exit 0 }
    }
    Remove-Item -LiteralPath $target -Recurse -Force
    Say '제거했습니다. 저장한 프리셋·설정은 %LOCALAPPDATA%\StormLegend에 남아 있습니다(필요 없으면 직접 지우세요).' 'Green'
    Pause-End
    exit 0
}
catch { Say ('제거하지 못했습니다: ' + $_.Exception.Message) 'Red'; Pause-End; exit 1 }
