param([string]$AutoCADPath='C:\Program Files\Autodesk\AutoCAD 2020',[string]$SourceDwg='')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
if(!$SourceDwg){$SourceDwg=Join-Path $projectRoot '범례템플릿.dwg'}
$SourceDwg=(Resolve-Path -LiteralPath $SourceDwg).Path
$before=(Get-FileHash -LiteralPath $SourceDwg -Algorithm SHA256).Hash
& dotnet build (Join-Path $PSScriptRoot 'SourceExport.csproj') -c Release "-p:AutoCADPath=$AutoCADPath" -p:NuGetAudit=false --ignore-failed-sources --nologo -m:1
if($LASTEXITCODE -ne 0){throw 'Exporter build failed'}
$out=Join-Path $projectRoot ('work\export-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out | Out-Null
$dllDir=(Join-Path $PSScriptRoot 'bin\Release').Replace('\','/')
$script=Join-Path $out 'export.scr'
[IO.File]::WriteAllLines($script,@('(setq ahj-old-tp (getvar "TRUSTEDPATHS"))',('(setvar "TRUSTEDPATHS" "'+$dllDir+'")'),'_.NETLOAD',('"'+$dllDir+'/SourceExport.dll"'),'LGENEXPORT','(setvar "TRUSTEDPATHS" ahj-old-tp)','_.QUIT','_N'),[Text.Encoding]::GetEncoding(949))
$oldSource=$env:LEGEND_MASTER_DWG;$oldOutput=$env:LEGEND_EXPORT_OUTPUT
try {
    $env:LEGEND_MASTER_DWG=$SourceDwg;$env:LEGEND_EXPORT_OUTPUT=$out
    $started=Get-Date
    $process=Start-Process -FilePath (Join-Path $AutoCADPath 'accoreconsole.exe') -ArgumentList ('/s "'+$script+'" /isolate LegendTemplateExport "'+$out+'"') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $out 'stdout.log') -RedirectStandardError (Join-Path $out 'stderr.log') -PassThru
    while(!$process.WaitForExit(5000)) {
        if((Test-Path -LiteralPath (Join-Path $out 'export-success.txt')) -or (Test-Path -LiteralPath (Join-Path $out 'export-error.txt'))){if(!$process.WaitForExit(2000)){Stop-Process -Id $process.Id};break}
        if(((Get-Date)-$started).TotalSeconds -gt 120){Stop-Process -Id $process.Id;throw 'Read-only template export timed out'}
    }
    if((Get-FileHash -LiteralPath $SourceDwg -Algorithm SHA256).Hash -ne $before){throw 'Source checksum changed'}
    if(!(Test-Path -LiteralPath (Join-Path $out 'export-success.txt'))){throw ('Export failed; inspect '+$out)}
    Write-Host ('PASS: read-only export and source checksum. Exported files: '+$out)
    Write-Host 'Review the export before replacing Template files and rebuilding mappings.'
}
finally {$env:LEGEND_MASTER_DWG=$oldSource;$env:LEGEND_EXPORT_OUTPUT=$oldOutput}
