# AutoCAD 2020 / .NET Framework 4.7.1 호환성

이 프로젝트는 사용자 요구에 따라 **C# / .NET Framework 4.7.1 / x64**를 대상으로 빌드합니다. .NET Core 또는 최신 .NET 프로젝트로 변경하지 않습니다.

## 대상 프레임워크와 실행 환경

Autodesk 공식 표는 AutoCAD 2020의 릴리스 번호를 **23.1**, 지원 Managed SDK를 **2020 및 2019**, .NET Framework를 **4.7**로 기재합니다. 따라서 본 프로젝트의 4.7.1은 사용자 지정 빌드 대상이며, Autodesk 표에 4.7.1이 별도로 명시된 것으로 해석하면 안 됩니다. [Autodesk 2020 호환성 표](https://help.autodesk.com/cloudhelp/2020/ENU/AutoCAD-Customization/files/GUID-A6C680F2-DE2E-418A-A182-E4884073338A.htm)

빌드에는 **4.7.1 참조 어셈블리/Targeting Pack**, 실행에는 **4.7.1 이상 .NET Framework runtime**이 필요합니다. Framework 4.x의 후속 버전은 기존 버전을 대체하는 업데이트이므로, 더 높은 runtime이 설치된 컴퓨터에 4.7.1 runtime을 추가 설치하거나 다운그레이드할 필요는 없습니다. 실행 runtime이 높더라도 프로젝트의 빌드 대상은 4.7.1로 유지됩니다. [Microsoft 버전 및 종속성](https://learn.microsoft.com/dotnet/framework/install/versions-and-dependencies)

이번 작업 환경에는 `C:\Program Files\Autodesk\AutoCAD 2020`의 AutoCAD 2020 및 `accoreconsole.exe`가 있으며, 확인된 .NET Framework runtime 파일 버전은 **4.8.09037**입니다. 이 환경에서의 성공은 4.7.1만 설치된 별도 PC의 실행 결과를 대신하지 않습니다. 실제 빌드 및 실행 검증 결과는 별도 검증 기록을 참고하십시오.

## AutoCAD 참조 및 원본 복제

- AutoCAD **2020**의 `AcMgd.dll`, `AcDbMgd.dll`, `AcCoreMgd.dll`을 참조합니다. 세 참조의 `Copy Local`은 `False`이며 Autodesk DLL을 배포물에 동봉하지 않습니다. [Autodesk 2020 참조 설정](https://help.autodesk.com/cloudhelp/2020/ENU/OARX-DevGuide-Managed/files/GUID-2363CE7C-AC2B-4CAC-AE5D-F77B386132D7.htm)
- DXF는 새 `Database(false, true)`에 `DxfIn`으로 읽습니다. Autodesk는 기본 도면이 이미 생성되었거나 데이터가 있는 Database에 `DxfIn`을 호출하면 치명적 오류가 발생할 수 있다고 명시합니다. [DxfIn API](https://help.autodesk.com/cloudhelp/2026/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_Database_DxfIn_string_string.html)
- 원본 기호와 표 테두리 엔티티는 `WblockCloneObjects`로 데이터베이스 사이에서 복제합니다. 원본 → 비어 있는 임시 도면 단계는 `DuplicateRecordCloning.Replace`로 템플릿 정의를 이름 그대로 가져오고(MangleName은 모든 이름에 `$0$`를 붙여 템플릿과 레이어 이름이 달라짐), 임시 도면 → 사용자 도면 단계는 `DuplicateRecordCloning.Ignore`로 이미 있는 같은 이름의 레이어·문자 스타일·블록을 다시 씁니다. 같은 도면에서 여러 번 생성해도 복제 정의가 늘어나지 않습니다. 범례는 블록 없이 개별 객체로 사용자 도면 공간에 복사합니다.

## 로드

템플릿 DWG는 `Database.ReadDwgFile(..., FileOpenMode.OpenForReadAndAllShare, ...)`로 화면에 열지 않고 읽어 분석합니다. 분석 전에 스냅샷으로 복사하므로 AutoCAD에서 템플릿을 연 상태여도 읽을 수 있습니다.

빌드한 DLL을 신뢰할 수 있는 로컬 폴더에 두고 AutoCAD 2020에서 `NETLOAD`로 불러옵니다. 필요한 경우 해당 폴더를 `TRUSTEDPATHS`에 등록합니다. Autodesk는 AutoCAD 2014 이후 보안 로드 모드에서 신뢰 위치를 사용하도록 안내합니다. [Autodesk 보안 로드 안내](https://help.autodesk.com/cloudhelp/2020/ENU/AutoCAD-Customization/files/GUID-A6C680F2-DE2E-418A-A182-E4884073338A.htm)
