# Autocad_auto

AutoCAD 자동화 명령 모음입니다. 명령마다 폴더 하나에 소스·템플릿·빌드 스크립트·검증 기록을 모두 담고, 화면은 공용 템플릿 하나를 함께 씁니다.

| 폴더 | 명령 | 내용 |
|---|---|---|
| [LegendAuto](LegendAuto/README.md) | `LEGEND`, `LEGENDALL`, `LEGENDORIGINAL`, `LEGENDTEMPLATE` | 통합 범례(공사·유역·우수·오수·상수·포장·부대) 생성 |
| [Shared/Ahjooeng](Shared/Ahjooeng/AhjooengMenu.cs) | — | 메뉴 막대 **Ahjooeng** 공용 모듈. 명령 DLL을 불러오면 자기 분류 하위 메뉴를 넣음 |
| [Shared/CadUi](Shared/CadUi/README.md) | — | 모든 명령 창의 공용 화면 템플릿(AutoCAD 스타일 탭·도구 막대·묶음 상자·아래 버튼) |

## 새 명령 만들기

1. 이 폴더 아래에 `새명령이름/`을 만들고 LegendAuto와 같은 구조(`src`, `tests`, `build.ps1`, `README.md`)를 따릅니다.
2. 창은 [Shared/CadUi](Shared/CadUi/README.md)의 `CadToolWindow`를 상속해 만듭니다. 그러면 LEGEND 창과 같은 모양·색·크기가 됩니다.
3. `Shared/Ahjooeng/*.cs`도 링크하고, 플러그인 시작(`IExtensionApplication.Initialize`) 때 `AhjooengMenu.Register(..., "분류 이름", 항목들, 공통 항목)`을 부르면 메뉴 막대 **Ahjooeng** 아래에 분류가 생깁니다(LegendAuto `Commands.RegisterMenu` 참고).
4. 위 표에 명령을 추가합니다.

다음 단계(메뉴 막대·리본에 명령 넣기) 기획: [docs/PLAN-menu-ribbon.md](docs/PLAN-menu-ribbon.md)
