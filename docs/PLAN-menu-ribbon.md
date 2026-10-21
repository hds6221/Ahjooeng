# 다음 단계 기획: AutoCAD 메뉴 막대·리본에 내 명령 넣기

작성일 2026-10-15 · 대상 AutoCAD 2020(R23.1) · 기준 화면: DreamPlus 메뉴(분류별 하위 메뉴 + 아래쪽 설정·도움말·프로그램 정보)

## 1. 목표

1. AutoCAD를 켜면 NETLOAD 없이 자동으로 불러옵니다.
2. 메뉴 막대에 **내 메뉴(이름 미정, 예: `Autocad_auto`)**가 생기고, 분류별 하위 메뉴에서 명령을 고릅니다.
3. 리본에도 같은 내용의 **탭**이 생깁니다(큰 아이콘 + 이름 + 설명 툴팁).
4. 새 명령을 만들 때 **목록 파일 한 줄만 추가**하면 메뉴와 리본에 함께 나타납니다.

## 2. 메뉴 구성(초안)

```
Autocad_auto                        ← 메뉴 막대 이름(결정 필요)
├─ 범례                ▸ 범례 만들기…                LEGEND
│                       한 공종 전체 넣기             LEGENDALL
│                       원본 표 그대로 복사           LEGENDORIGINAL
│                       ─────────────
│                       범례 템플릿 수정…             LEGENDTEMPLATE
├─ (다음 명령 분류)    ▸ …
├─ ─────────────
├─ 설정…                            (명령별 설정 창 모음)
├─ 도움말                            (README를 쉬운 화면으로)
└─ 프로그램 정보                     (버전 · 설치 위치 · 템플릿 상태)
```

리본 탭 `Autocad_auto`는 분류마다 패널 하나를 둡니다(예: [범례] 패널: 큰 버튼 "범례 만들기" + 작은 버튼 3개). 아이콘은 공용 화면 템플릿의 `CadGlyph`로 그려 이미지 파일 없이 만들고, 필요하면 나중에 PNG로 바꿉니다.

## 3. 구조

```
Autocad_auto/
├─ Shell/                    ← 새로 만들 "껍데기" 플러그인(AutocadAuto.Shell.dll)
│  ├─ 자동 로드 · 메뉴 막대 · 리본 · 공통 설정/도움말/정보 창
│  └─ menu.json 들을 모아 메뉴를 만듦
├─ LegendAuto/menu.json      ← 각 명령 폴더가 자기 메뉴 항목을 적음
├─ (새명령)/menu.json
├─ Shared/CadUi/             ← 공용 화면 템플릿(이미 있음)
└─ deploy/AutocadAuto.bundle/← 설치 폴더(PackageContents.xml + 각 DLL)
```

`menu.json` 예시(명령마다 한 줄씩 추가):

```json
{ "group": "범례", "order": 10, "items": [
  { "label": "범례 만들기…",       "command": "LEGEND",         "icon": "Generate", "ribbon": "large", "tip": "여러 공종을 골라 한 번에 범례를 그립니다" },
  { "label": "한 공종 전체 넣기",   "command": "LEGENDALL",      "icon": "New" },
  { "label": "원본 표 그대로 복사", "command": "LEGENDORIGINAL", "icon": "Template" },
  { "separator": true },
  { "label": "범례 템플릿 수정…",   "command": "LEGENDTEMPLATE", "icon": "Template", "tip": "템플릿 DWG를 열어 항목을 추가합니다" } ] }
```

## 4. 방식 선택(추천안)

| 항목 | 방법 | 이유 |
|---|---|---|
| 자동 로드 | `%APPDATA%\Autodesk\ApplicationPlugins\AutocadAuto.bundle` + `PackageContents.xml`(LoadOnAutoCADStartup) | AutoCAD 공식 방식. 이 폴더는 신뢰 위치로 처리되어 보안 경고 없음. NETLOAD·TRUSTEDPATHS 불필요 |
| 명령 DLL 불러오기 | Shell이 각 폴더의 DLL을 `ExtensionLoader.Load`로 로드 | 명령 추가 시 Shell을 다시 빌드할 필요 없음 |
| 메뉴 막대 | 실행 중 COM(`MenuGroups → Menus.Add → InsertInMenuBar`)으로 매번 생성 | 사용자의 acad.cuix를 고치지 않음 → 지우면 깨끗이 사라짐. DreamPlus 등 다른 메뉴와 충돌 없음 |
| 리본 | `AdWindows.dll`의 `ComponentManager.Ribbon`에 탭 추가(실행 중 생성) | CUIx 편집 없이 동적으로 만들 수 있고, 아이콘·툴팁을 코드로 관리 |
| 버튼 동작 | `SendStringToExecute("_LEGEND ")` | 명령줄에서 친 것과 똑같이 실행되어 UNDO·명령 반복(Enter)이 정상 동작 |

대안: 부분 CUIx 파일을 만들어 `CUILOAD`로 붙이는 방법도 있습니다. 작업공간에 고정할 수 있지만 파일 관리가 늘어나고, 사용자가 CUI를 직접 편집할 때 섞이기 쉬워 2차 선택지로 둡니다.

## 5. 대비해야 할 문제

- **리본 준비 시점**: AutoCAD 시작 직후에는 리본이 아직 없을 수 있습니다. `ComponentManager.ItemInitialized`를 기다렸다가 탭을 만들고, 작업공간을 바꾸거나(WSCURRENT) 리본을 껐다 켜면 다시 붙입니다.
- **메뉴 막대가 꺼진 경우**(MENUBAR=0): 메뉴는 만들어 두고, 명령줄에 "메뉴 막대를 보려면 MENUBAR 1"을 한 번 안내합니다. 리본으로도 같은 기능을 쓸 수 있습니다.
- **중복 로드**: 기존처럼 NETLOAD로 직접 불러온 경우와 겹치지 않게, 이미 로드된 어셈블리는 다시 불러오지 않습니다.
- **명령 이름 충돌**: DreamPlus 등 다른 프로그램 명령과 겹치는지 시작할 때 검사하고, 겹치면 경고합니다. 필요하면 공통 접두어(예: `AA_`)를 붙인 별칭을 둡니다.
- **업데이트 중 DLL 잠김**: AutoCAD가 켜져 있으면 DLL을 덮어쓸 수 없습니다(이번에 겪은 문제). 설치 스크립트는 AutoCAD 실행 여부를 확인해 "닫고 다시 실행하세요"로 안내하거나, 다음 시작 때 바꿔 끼우는 방식으로 만듭니다.
- **FILEDIA/CMDDIA**: Shell 시작 시 0이면 1로 되돌리는 기능을 공통으로 옮깁니다(현재 LegendAuto에 있음).
- **검증**: Core Console에는 메뉴·리본이 없으므로, 메뉴 구성(menu.json → 메뉴 모델)은 자동 검사하고, 실제 표시는 점검표로 확인합니다.

## 6. 진행 단계

| 단계 | 내용 | 완료 기준 |
|---|---|---|
| 1. 껍데기 + 자동 로드 | `Shell` 프로젝트, `.bundle` 구조, 설치/제거 스크립트, LegendAuto를 Shell이 로드 | AutoCAD 시작만으로 `LEGEND` 실행 가능 |
| 2. 메뉴 막대 | menu.json 읽기 → 분류별 하위 메뉴 + 설정·도움말·프로그램 정보 | DreamPlus처럼 메뉴 막대에서 실행 |
| 3. 리본 탭 | 분류별 패널, 큰/작은 버튼, 아이콘·툴팁, 작업공간 전환 대응 | 리본에서 실행, 다시 켜도 유지 |
| 4. 공통 창 | 프로그램 정보(버전·템플릿 상태), 도움말, 명령별 설정 모음 | 처음 쓰는 사람이 메뉴만으로 사용 가능 |
| 5. 배포 | 설치 ZIP(압축 풀고 install.ps1 한 번), 버전 표시, 제거 | 다른 PC에 설치·제거 확인 |

> **2026-10-21 현재 결정(이 문서의 예전 계획보다 우선):** 메뉴는 분류 없이 명령만 — `범례 생성 (LGD)`(바로 실행) + 구분선 + `도움말`(바로 도움말 홈=명령 목록, `AHJOOENGHELP`) + `프로그램 정보`(`AHJOOENGINFO`). 도움말은 `Autocad_auto/Help`(명령 목록 홈, `commands.js`에 명령 한 줄씩 추가) → 설치 시 `Contents/Help`. 리본·설정 메뉴는 쓰지 않음. 처음 켤 때 AutoCAD 보안 창("서명되지 않은 실행 파일")이 뜸(ApplicationPlugins도 신뢰 위치 아님) → "항상 로드".

## 6-1. 진행 현황 (2026-10-15)

- 메뉴 이름 **Ahjooeng**, **메뉴 막대만**(리본 없음)으로 결정.
- 2단계 일부 완료: `Shared/Ahjooeng/AhjooengMenu.cs`(공용) — 명령 DLL을 NETLOAD하면 `Ahjooeng ▸ 범례` 하위 메뉴와 맨 아래 `Ahjooeng 도움말 · 프로그램 정보`를 만듭니다. 실행 중인 AutoCAD 2020에서 COM으로 생성·중복 방지·다른 분류 끼워 넣기를 확인했습니다.
- 1단계 완료(1.0): `Ahjooeng.bundle`(`LegendAuto/deploy/PackageContents.xml`, LoadOnAutoCADStartup) + `설치.bat`/`제거.bat`. 번들 이름은 계획의 `AutocadAuto.bundle` 대신 메뉴 이름과 같은 `Ahjooeng.bundle`.
- 남은 일: 공통 항목(도움말·프로그램 정보)을 Shell로 옮기기(두 번째 명령이 생길 때).

## 7. 정해 주셔야 할 것

1. ~~메뉴 이름~~ → Ahjooeng
2. ~~리본 여부~~ → 메뉴 막대만
3. AutoCAD 시작 시 자동 로드 여부(추천: 자동)
4. 다음에 만들 명령 분류 이름(메뉴 분류를 미리 잡아 두면 좋습니다)
