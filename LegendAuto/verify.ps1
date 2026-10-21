# AutoCAD Core Console에서 실제 생성 검증을 실행합니다.

# -Release : NETLOAD로 검사할 빌드 폴더(기본 bin\Release). AutoCAD가 DLL을 잡고 있으면 다른 폴더(예: bin\Next)를 씁니다.

# -SkipBuild: 이미 빌드한 결과를 그대로 검사합니다.

# 통과 조건: 결과 파일마다 명시적인 성공 표식(SUCCESS:/PASS:)이 있고 FAIL이 하나도 없어야 합니다.

# 검사한 DLL·템플릿의 해시와 실행 환경은 verification\evidence-native.json에 남깁니다.

param([string]$AutoCADPath='C:\Program Files\Autodesk\AutoCAD 2020',[switch]$SkipBuild,[string]$Release='')

$ErrorActionPreference='Stop'

$releaseRoot=if(!$Release){Join-Path $PSScriptRoot 'bin\Release'}elseif([IO.Path]::IsPathRooted($Release)){[IO.Path]::GetFullPath($Release)}else{[IO.Path]::GetFullPath((Join-Path $PSScriptRoot $Release))}

if (!$SkipBuild) {

    & (Join-Path $PSScriptRoot 'build.ps1') -AutoCADPath $AutoCADPath -OutDir $releaseRoot

    # 검증 도구도 같은 폴더의 Core를 참조해 빌드합니다(사용자 지정 AutoCADPath 전달).

    & dotnet build (Join-Path $PSScriptRoot 'tests\StormLegend.Verify.csproj') -c Release "-p:AutoCADPath=$AutoCADPath" "-p:LegendOut=$releaseRoot\" -p:NuGetAudit=false --ignore-failed-sources --nologo -m:1

    if($LASTEXITCODE -ne 0){throw 'Verification build failed'}

}

$verifyRoot=Join-Path $PSScriptRoot 'bin\Verify'

foreach($required in @("$releaseRoot\StormLegend.dll","$releaseRoot\StormLegend.Core.dll","$verifyRoot\StormLegend.Verify.dll","$verifyRoot\StormLegend.Core.dll")){ if(!(Test-Path -LiteralPath $required)){throw "Missing $required"} }

$coreRelease=(Get-FileHash -LiteralPath "$releaseRoot\StormLegend.Core.dll" -Algorithm SHA256).Hash

$coreVerify=(Get-FileHash -LiteralPath "$verifyRoot\StormLegend.Core.dll" -Algorithm SHA256).Hash

# Core Console에서는 먼저 불러온 검증 도구의 Core가 플러그인에도 쓰이므로, 두 Core가 같아야 검증이 배포 파일을 대표합니다.

if($coreRelease -ne $coreVerify){throw "StormLegend.Core.dll differs between $releaseRoot and bin\Verify. Rebuild without -SkipBuild."}



$temporary=Join-Path $PSScriptRoot 'work\verification'

$verification=Join-Path $PSScriptRoot 'verification'

New-Item -ItemType Directory -Force -Path $temporary,$verification | Out-Null

$verifyDir=$verifyRoot.Replace('\','/'); $releaseDir=$releaseRoot.Replace('\','/')

$script=@('(setq ahj-old-tp (getvar "TRUSTEDPATHS"))',('(setvar "TRUSTEDPATHS" "'+$verifyDir+';'+$releaseDir+'")'),'_.NETLOAD',('"'+$verifyDir+'/StormLegend.Verify.dll"'),'LEGENDMULTIVERIFY',

    '_.NETLOAD',('"'+$releaseDir+'/StormLegend.dll"'),'LEGENDBASELINE','_.UNDO','_Mark','LEGENDALL','7','300','0,0,0','LEGENDCLICHECK','_.UNDO','_Back','LEGENDUNDOCHECK','(setvar "TRUSTEDPATHS" ahj-old-tp)','_.QUIT','_Y')

$scriptPath=Join-Path $temporary 'verify.scr'

[IO.File]::WriteAllLines($scriptPath,$script,[Text.Encoding]::GetEncoding(949))

$checks=@(@{File='runtime-verification.txt';Marker='(?m)^SUCCESS: \d+ native checks'},@{File='command-verification.txt';Marker='^PASS:'},@{File='undo-verification.txt';Marker='^PASS:'})

foreach($check in $checks){$file=Join-Path $verification $check.File;if(Test-Path -LiteralPath $file){Remove-Item -LiteralPath $file}}

$old=$env:LEGEND_PROJECT_ROOT

try {

    $env:LEGEND_PROJECT_ROOT=$PSScriptRoot

    $started=Get-Date

    $process=Start-Process -FilePath (Join-Path $AutoCADPath 'accoreconsole.exe') -ArgumentList ('/s "'+$scriptPath+'" /isolate LegendMultiVerify "'+$temporary+'"') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $temporary 'stdout.log') -RedirectStandardError (Join-Path $temporary 'stderr.log') -PassThru

    while(!$process.WaitForExit(5000)) {

        $complete=$true

        foreach($check in $checks){if(!(Test-Path -LiteralPath (Join-Path $verification $check.File))){$complete=$false}}

        if($complete){if(!$process.WaitForExit(3000)){Stop-Process -Id $process.Id};break}

        if(((Get-Date)-$started).TotalSeconds -ge 900){Stop-Process -Id $process.Id;throw 'Native verification timed out; inspect work/verification/stdout.log'}

    }

    $summary=@()

    foreach($check in $checks){

        $file=Join-Path $verification $check.File

        if(!(Test-Path -LiteralPath $file)){throw ('Missing result '+$check.File+' (see work/verification/stdout.log)')}

        $text=Get-Content -LiteralPath $file -Raw -Encoding UTF8

        if($text -match '(?m)^FAIL\b'){throw ('Verification failed: '+$check.File)}

        if($text -notmatch $check.Marker){throw ('No success marker in '+$check.File)}

        $summary+=[string]($text -split "`r?`n" | Where-Object { $_ -match '^(SUCCESS|PASS):' } | Select-Object -Last 1)

    }

    # 두 AutoCAD 프로세스가 같은 고친 템플릿을 동시에 분석(캐시 경쟁 조건)
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'tests\concurrency-test.ps1') -AutoCADPath $AutoCADPath | Out-Null
    $concurrency=Get-Content -LiteralPath (Join-Path $verification 'concurrency-verification.txt') -Raw -Encoding UTF8
    if($LASTEXITCODE -ne 0 -or $concurrency -match '(?m)^FAIL:' -or $concurrency -notmatch '(?m)^SUCCESS: \d+ concurrency checks'){throw 'Concurrency verification failed: verification\concurrency-verification.txt'}
    $summary+=[string]($concurrency -split "`r?`n" | Where-Object { $_ -match '^SUCCESS:' } | Select-Object -Last 1)
    $loaded=@(Get-Content -LiteralPath (Join-Path $verification 'command-verification.txt') -Encoding UTF8 | Where-Object { $_ -like 'LOADED:*' } | ForEach-Object { [string]$_ })

    $hash={param($p) (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant()}

    $evidence=[ordered]@{

        kind='native'; finished=(Get-Date).ToString('s'); computer=$env:COMPUTERNAME

        autocad=(Get-Item -LiteralPath (Join-Path $AutoCADPath 'accoreconsole.exe')).VersionInfo.ProductVersion

        dotnetFramework=(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction SilentlyContinue).Version

        releaseFolder=$releaseRoot

        files=[ordered]@{

            'StormLegend.dll'=(& $hash "$releaseRoot\StormLegend.dll"); 'StormLegend.Core.dll'=$coreRelease.ToLowerInvariant()

            'StormLegend.Verify.dll'=(& $hash "$verifyRoot\StormLegend.Verify.dll"); 'Template/범례템플릿.dwg'=(& $hash (Join-Path $releaseRoot 'Template\범례템플릿.dwg'))

            'Template/범례템플릿.dxf'=(& $hash (Join-Path $releaseRoot 'Template\범례템플릿.dxf')); 'Template/preview-catalog.json'=(& $hash (Join-Path $releaseRoot 'Template\preview-catalog.json'))

            'Template/catalog-index.json'=(& $hash (Join-Path $releaseRoot 'Template\catalog-index.json'))

        }

        loadedInAutoCAD=$loaded; results=$summary

    }

    ($evidence | ConvertTo-Json -Depth 5) | Set-Content -LiteralPath (Join-Path $verification 'evidence-native.json') -Encoding UTF8

    Write-Host ('PASS: native verification, actual NETLOAD command workflow and UNDO. ' + ($summary -join ' / '))

}

finally {$env:LEGEND_PROJECT_ROOT=$old}

