# 개발용: Template/preview-catalog.json을 C# 분석기(TemplateAnalyzer)로 다시 만듭니다. 원본 DWG/DXF는 바꾸지 않습니다.
# -OutDir: Core를 빌드할 폴더(기본 bin\Release). AutoCAD가 DLL을 잡고 있으면 bin\Next 등을 씁니다.
param([string]$AutoCADPath='C:\Program Files\Autodesk\AutoCAD 2020',[string]$OutDir='')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$out=if(!$OutDir){Join-Path $root 'bin\Release'}elseif([IO.Path]::IsPathRooted($OutDir)){$OutDir}else{[IO.Path]::GetFullPath((Join-Path $root $OutDir))}
& dotnet build (Join-Path $root 'tests\StormLegend.Verify.csproj') -c Release "-p:AutoCADPath=$AutoCADPath" "-p:LegendOut=$out\" -p:NuGetAudit=false --ignore-failed-sources --nologo -m:1
if($LASTEXITCODE -ne 0){throw 'build failed'}
$work=Join-Path $root 'work'; New-Item -ItemType Directory -Force $work | Out-Null
$report=Join-Path $work 'rebuild-preview.txt'; if(Test-Path $report){Remove-Item $report}
$dll=(Join-Path $root 'bin\Verify').Replace('\','/')
$scr=Join-Path $work 'rebuild-preview.scr'
[IO.File]::WriteAllLines($scr,@('(setq ahj-old-tp (getvar "TRUSTEDPATHS"))',('(setvar "TRUSTEDPATHS" "'+$dll+'")'),'_.NETLOAD',('"'+$dll+'/StormLegend.Verify.dll"'),'LEGENDREBUILDPREVIEW','(setvar "TRUSTEDPATHS" ahj-old-tp)','_.QUIT','_N'),[Text.Encoding]::GetEncoding(949))
$old=$env:LEGEND_PROJECT_ROOT
try {
    $env:LEGEND_PROJECT_ROOT=$root
    $p=Start-Process -FilePath (Join-Path $AutoCADPath 'accoreconsole.exe') -ArgumentList ('/s "'+$scr+'" /isolate LegendRebuildPreview "'+$work+'"') -WindowStyle Hidden -PassThru
    $start=Get-Date
    while(!(Test-Path $report)){ if(((Get-Date)-$start).TotalSeconds -gt 120){Stop-Process -Id $p.Id; throw 'timeout'}; Start-Sleep -Milliseconds 500 }
    if(!$p.WaitForExit(5000)){Stop-Process -Id $p.Id}
    $text=Get-Content $report -Raw -Encoding UTF8
    Write-Host $text
    if($text -notmatch '^PASS:'){throw 'Preview rebuild failed (see work\rebuild-preview.txt)'}
}
finally {$env:LEGEND_PROJECT_ROOT=$old}
