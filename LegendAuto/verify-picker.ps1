# 숨긴 창으로 LEGEND 화면을 검사하고 화면 그림·설명서 그림을 만듭니다.
# -Release: 검사할 빌드 폴더(기본 bin\Release). AutoCAD가 DLL을 잡고 있을 때는 다른 폴더로 빌드해 검사합니다.
# 통과 조건: 'SUCCESS: n picker checks' 표식이 있고 FAIL이 없어야 합니다. 검사한 파일 해시는 verification\evidence-ui.json에 남깁니다.
param([string]$AutoCADPath='C:\Program Files\Autodesk\AutoCAD 2020',[string]$Release='')
$ErrorActionPreference='Stop'
$release=if(!$Release){Join-Path $PSScriptRoot 'bin\Release'}elseif([IO.Path]::IsPathRooted($Release)){[IO.Path]::GetFullPath($Release)}else{[IO.Path]::GetFullPath((Join-Path $PSScriptRoot $Release))}
$output=Join-Path $PSScriptRoot 'verification'
$oldError=Join-Path $output 'picker-error.txt'
if(Test-Path -LiteralPath $oldError){Remove-Item -LiteralPath $oldError}
$result=Join-Path $output 'picker-verification.txt'
if(Test-Path -LiteralPath $result){Remove-Item -LiteralPath $result}
$work=Join-Path $PSScriptRoot ('work\picker-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $work,$output | Out-Null
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe=Join-Path $work 'PickerPreview.exe'
& $csc /nologo /target:exe /platform:x64 "/out:$exe" "/reference:$release\StormLegend.dll" "/reference:$release\StormLegend.Core.dll" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll /reference:System.Runtime.Serialization.dll (Join-Path $PSScriptRoot 'tests\PickerPreview.cs')
if($LASTEXITCODE -ne 0){throw 'Picker harness compile failed'}
$help=Join-Path $PSScriptRoot 'Help\images'
& $exe $release $output $work $AutoCADPath $help
if($LASTEXITCODE -ne 0){throw 'Picker verification failed; see verification/picker-error.txt'}
if(!(Test-Path -LiteralPath $result)){throw 'No picker result file'}
if(Test-Path -LiteralPath $oldError){throw 'picker-error.txt exists'}
$text=Get-Content -LiteralPath $result -Raw -Encoding UTF8
if($text -match '(?m)^FAIL\b' -or $text -notmatch '(?m)^SUCCESS: \d+ picker checks'){throw 'Picker verification has no success marker'}
$hash={param($p) (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant()}
$lines=$text -split "`r?`n"
$evidence=[ordered]@{
    kind='ui'; finished=(Get-Date).ToString('s'); computer=$env:COMPUTERNAME; releaseFolder=$release
    files=[ordered]@{ 'StormLegend.dll'=(& $hash "$release\StormLegend.dll"); 'StormLegend.Core.dll'=(& $hash "$release\StormLegend.Core.dll"); 'Template/preview-catalog.json'=(& $hash "$release\Template\preview-catalog.json") }
    loaded=@($lines | Where-Object { $_ -like 'LOADED:*' }); result=@($lines | Where-Object { $_ -like 'SUCCESS:*' })
}
($evidence | ConvertTo-Json -Depth 4) | Set-Content -LiteralPath (Join-Path $output 'evidence-ui.json') -Encoding UTF8
$lines | Where-Object { $_ -like 'SUCCESS:*' }
