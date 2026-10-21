# Ahjooeng 메뉴 코드 검사(AutoCAD 없이 실행). 결과: SUCCESS: n menu checks.
$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$out = Join-Path $env:TEMP 'ahjooeng-menu-test'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$exe = Join-Path $out 'MenuVerification.exe'
& $csc -nologo -warn:0 -codepage:65001 -platform:x64 -r:Microsoft.CSharp.dll "-out:$exe" (Join-Path $PSScriptRoot 'MenuVerification.cs') (Join-Path $PSScriptRoot '..\AhjooengMenu.cs')
if ($LASTEXITCODE -ne 0) { throw 'menu test build failed' }
[Console]::OutputEncoding = [Text.Encoding]::UTF8
& $exe
if ($LASTEXITCODE -ne 0) { throw 'menu test failed' }
