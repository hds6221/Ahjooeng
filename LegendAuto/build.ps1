param(
    [string]$AutoCADPath = 'C:\Program Files\Autodesk\AutoCAD 2020',
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    # 빌드 결과 폴더(기본 bin\Release). AutoCAD가 bin\Release의 DLL을 잡고 있으면 다른 폴더(예: bin\Next)로 빌드합니다.
    [string]$OutDir = ''
)
$ErrorActionPreference = 'Stop'
foreach ($assembly in @('AcMgd.dll','AcDbMgd.dll','AcCoreMgd.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $AutoCADPath $assembly))) { throw "AutoCAD 2020 reference missing: $assembly ($AutoCADPath)" }
}
$projectPath = Join-Path $PSScriptRoot 'src\StormLegend\StormLegend.csproj'
$out = if (!$OutDir) { Join-Path $PSScriptRoot "bin\$Configuration" } elseif ([IO.Path]::IsPathRooted($OutDir)) { [IO.Path]::GetFullPath($OutDir) } else { [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $OutDir)) }
& dotnet build $projectPath -c $Configuration '-p:Platform=x64' "-p:AutoCADPath=$AutoCADPath" "-p:LegendOut=$out\" -p:NuGetAudit=false --ignore-failed-sources --nologo -m:1
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Host "NETLOAD: $(Join-Path $out 'StormLegend.dll')"
