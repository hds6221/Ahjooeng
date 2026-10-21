# 인수인계 메모 (HANDOFF)

마지막 작업: 2026-10-21 · 버전 **Ahjooeng 1.0.0 (첫 배포)** · 작성: Claude Code

> 새 컴퓨터의 Claude에게: 이 파일을 먼저 끝까지 읽고, 아래 "작업 규칙"을 지키며 "남은 일"부터 이어서 진행하세요.

---

## 1. 프로젝트 한눈에 보기

AutoCAD 2020용 자동화 명령 모음 **Ahjooeng**. 지금은 명령 하나(**범례 생성, `LGD`**)가 완성되어 있고, 앞으로 명령을 계속 추가할 예정입니다.

- 범례 생성: `범례템플릿.dwg`의 공종 표(공사·유역·우수·오수·상수·포장·부대)에서 항목을 골라 도면에 범례를 그림. 결과는 블록이 아닌 개별 선·문자(설정에서 블록 선택 가능).
- 사용자는 처음 쓰는 사람도 이해할 수 있는 **쉬운 한국어 설명**을 원합니다(UI 문구·답변 모두).

### 기술 조건 (바꾸지 말 것)

- AutoCAD 2020(R23.1) · C# 7.3 · .NET Framework 4.7.1 · x64 · WinForms
- 빌드: `dotnet build`(.NET SDK, 현재 9.0) — `build.ps1`이 호출
- AutoCAD 참조 DLL: `C:\Program Files\Autodesk\AutoCAD 2020\AcMgd.dll` 등(경로가 다르면 `-AutoCADPath`)

### 폴더 구조

```
Autocad_auto/
├─ HANDOFF.md            ← 이 파일
├─ README.md             ← 명령 목록(새 명령을 만들면 여기 표에 추가)
├─ Help/                ← Ahjooeng 도움말 홈(명령 목록). 새 명령은 commands.js에 한 줄 추가
│                          설치 시 Contents/Help, 각 명령 설명서는 Contents/<명령폴더>/Help/index.html
├─ docs/PLAN-menu-ribbon.md   ← 메뉴 막대 계획·진행 현황
├─ Shared/
│  ├─ CadUi/             ← 공용 화면 템플릿(모든 새 명령 창은 이것으로)
│  └─ Ahjooeng/AhjooengMenu.cs  ← 공용 메뉴 막대 코드
└─ LegendAuto/           ← 범례 명령(명령 하나 = 폴더 하나)
   ├─ src/StormLegend/   ← 플러그인(StormLegend.dll) + Core(StormLegend.Core.dll)
   ├─ tests/             ← 검증 코드(Core Console 검사, 화면 검사)
   ├─ Template/          ← 배포용 범례템플릿.dwg + 분석 결과 json
   ├─ Help/index.html    ← 그림 사용 설명서
   ├─ deploy/            ← 설치 묶음(PackageContents.xml, install.ps1, 설치.bat/제거.bat)
   ├─ tools/package_release.py  ← 배포 zip 만들기
   ├─ build.ps1 / verify.ps1 / verify-picker.ps1
   ├─ README.md · VERIFICATION.md · TEMPLATE_UPDATE.md · COMPATIBILITY.md
   └─ 배포/              ← 만들어진 설치 zip·전체소스 zip
```

---

## 2. 작업 규칙 (사용자가 정한 것)

1. **명령 하나 = 폴더 하나.** 새 명령은 `Autocad_auto/<이름>/`에 src·tests·build.ps1·README.md를 두고, 루트 README.md 표에 등록.
2. **모든 새 명령 창은 `Shared/CadUi`의 `CadToolWindow`로** 만든다(사용자가 준 시안 기준: 탭 줄·아이콘 도구 막대·묶음 상자·아래 버튼). `.cs` 파일을 프로젝트에 링크(공용 DLL 만들지 않음). 참고 구현: `LegendAuto/src/StormLegend/MultiLegendForm.cs`, `CategoryPanel.cs`. 규칙은 `Shared/CadUi/README.md`.
3. 색은 **자연스럽고 차분하게**(채도 높은 파란 선택색 싫어함), 선택한 것은 잘 보이게.
4. **메뉴 막대만**(리본 없음), 메뉴 이름 **Ahjooeng**. 메뉴 모양:
   ```
   [Ahjooeng]
     범례 생성 (LGD)     ← 누르면 바로 실행
     (다음 명령 …)
     ─────────
     도움말              ← 바로 도움말 홈(명령 목록) → 명령을 눌러야 사용 설명서 (AHJOOENGHELP)
     프로그램 정보        (AHJOOENGINFO)
   ```
   새 명령은 `Ahjooeng.AhjooengMenu.Register(app, new MenuCommand("이름 (명령)", "명령"))` 한 줄로 메뉴에, `Help/commands.js` 한 줄로 도움말 목록에 추가된다.
   메뉴 코드 검사(AutoCAD 없이): `Shared/Ahjooeng/tests/run-menu-test.ps1`.
   `AHJOOENGHELP`·`AHJOOENGINFO`는 지금 범례 DLL에 들어 있음 → 두 번째 명령 DLL을 만들 때 같은 명령을 또 정의하면 충돌하므로 공용 Shell로 옮길 것.
5. **FILEDIA·CMDDIA를 절대 자동으로 바꾸지 않는다**(0이면 알리기만, 되돌리기는 `AHJOOENGDIA`).
6. 사용자의 기존 도면 객체·레이어·스타일을 임의로 바꾸지 않는다. 같은 이름 정의를 자동 덮어쓰지 않는다.
7. 고친 뒤에는 **검사 두 개가 모두 통과**해야 한다(아래 4장). 확인 안 된 것은 "확인 안 됨"이라고 분명히 말한다.

---

## 3. 새 컴퓨터 준비

1. 설치: AutoCAD 2020(기본 위치), .NET SDK, Python 3, Claude 데스크톱 앱.
2. `Ahjooeng_v1.0_전체소스.zip`을 원하는 곳에 풀기 → `Autocad_auto` 폴더 구조 그대로 유지(`LegendAuto`가 옆의 `Shared`를 씀).
3. (선택) 이전 PC에서 직접 고친 템플릿·프리셋 복사:
   - 템플릿: `%APPDATA%\Autodesk\ApplicationPlugins\Ahjooeng.bundle\Contents\LegendAuto\Template\범례템플릿.dwg`
   - 프리셋: `%LOCALAPPDATA%\StormLegend\presets-*.json`
4. Claude Code를 `Autocad_auto` 폴더에서 열고 "HANDOFF.md 읽고 이어서 해줘".

---

## 4. 빌드 · 검사 · 배포 (LegendAuto 폴더에서)

| 할 일 | 명령 | 결과 |
|---|---|---|
| 빌드 | `powershell -ExecutionPolicy Bypass -File build.ps1 -OutDir bin\Final` | 경고 0 · 오류 0 |
| 실제 생성 검사(Core Console) | `powershell -ExecutionPolicy Bypass -File verify.ps1 -Release bin\Final` | 마지막 결과 **12,832건 통과** + NETLOAD→LEGENDALL 434개 객체 + UNDO 복귀 + 두 프로세스 동시 분석 8건 |
| 화면 검사(숨긴 창) | `powershell -ExecutionPolicy Bypass -File verify-picker.ps1 -Release bin\Final` | 마지막 결과 **193건 통과**(배율 125~200% 모의 포함) |
| 설치 시험 | `package_release.py`가 자동 실행(`tests/install-test.ps1`, 가짜 APPDATA) | **19건 통과** |
| 배포 zip | `python tools/package_release.py` | 검증한 DLL 해시와 같을 때만 `배포/` zip 생성 |
| 이 PC에 설치 | AutoCAD 닫고 설치 zip 풀어 `설치.bat` (또는 `install.ps1 -Quiet`) | `%APPDATA%\Autodesk\ApplicationPlugins\Ahjooeng.bundle` |

주의:
- AutoCAD가 켜져 있으면 `bin\Release`의 DLL이 잠겨 빌드가 실패할 수 있음 → `-OutDir bin\Final`처럼 다른 폴더로 빌드.
- 검사 순서: verify.ps1 → verify-picker.ps1 → package_release.py (중간에 다시 빌드하면 해시가 달라져 패키징이 멈춤 — 정상 동작).
- PowerShell 스크립트는 **UTF-8 BOM**으로 저장해야 한글 경로가 깨지지 않음.
- Core Console 검사 스크립트(.scr)는 cp949로 저장.
- 템플릿 DWG를 Core Console로 열어 볼 때는 반드시 복사본이나 `/readonly` 사용(원본을 덮어쓴 적 있음).

---

## 5. 지금까지 한 일 (1.0 배포 전 개발 요약)

- 코드 리뷰 1~9 수정: 프리셋 항목 오연결(DWG 핸들 기준 재연결), 미리보기 데이터 검증, 부대 32행 명칭, 템플릿별 복구, 복사 실패 시 정리, 같은 이름 정의 충돌 알림/`_범례` 분리, 블록 생성 선종류 축척, FILEDIA 자동 변경 제거. 상세: `LegendAuto/VERIFICATION.md`.
- 자동 로드 설치 묶음(`Ahjooeng.bundle`) + 설치.bat/제거.bat. 사용자가 고친 템플릿은 설치 때 덮어쓰지 않음.
- 기호 그림 선명하게(크게 그리기, 밝은 색 보정, 마우스 올리면 크게 보기 창).
- 템플릿에 '범례' 레이어로 그린 기호 폴리선이 빠지던 오류 수정(행·열 경계 위 선만 표 테두리로 판단). 분석 캐시 버전 `a4`.
- 메뉴 정리: `범례 생성 (LGD)` + `도움말 ▸ 범례 생성 (LGD)`만.
- 시작 진단 기록: 플러그인이 켜질 때 `%LOCALAPPDATA%\StormLegend\startup.txt`에 불러온 시각·DLL 위치·메뉴 모양을 씀.

### 실제 AutoCAD에서 확인된 것 (2026-10-15 17:19)

```
dll: %APPDATA%\Autodesk\ApplicationPlugins\Ahjooeng.bundle\Contents\LegendAuto\StormLegend.dll
version: 1.0.0.0
menu: 범례 생성 (LGD)=LGD | --- | 도움말 ▸ [범례 생성 (LGD)=LEGENDHELP]
```
→ NETLOAD 없이 자동 로드, 메뉴 모양 정상. 단 처음 켤 때 **"보안 - 서명되지 않은 실행 파일"** 창이 떴고 사용자가 "항상 로드"를 선택함.

---

### 2026-10-21

- 도움말 구조 변경: 홈 = 명령 목록(분류 없이 명령만, DreamPlus 도움말의 **구성만** 참고·UI는 참고 금지). 왼쪽 목록은 모든 페이지 공통(`Help/nav.js`), 지금 보는 명령은 펼쳐서 항목 표시.
- 사용자가 "템플릿에 추가했는데 안 뜬다" → 원인: 개발 폴더 `bin\Release`의 **개발 중 예전 DLL을 NETLOAD**해서 씀(예전 메뉴·예전 도움말·예전 분석기 버그). v5.1 분석기는 그 템플릿의 '삼각형' 기호(B7A)를 정상 인식(`LEGENDANALYZEDWG` 진단 명령으로 확인). 사용자가 그날 고친 `bin\Release\Template\범례템플릿.dwg`를 설치본 템플릿으로 옮김(이전 설치본 템플릿은 `Ahjooeng.bundle.이전설치`에 남음).
- 재발 방지: 플러그인이 켜질 때 다른 위치의 StormLegend.dll이 함께 불러와졌거나 설치본이 아닌 DLL이면 명령줄 경고 + `startup.txt`에 `warning:` 기록.
- 검사 스크립트가 끝날 때 TRUSTEDPATHS를 원래 값으로 되돌리도록 수정. 사용자 실제 프로필의 TRUSTEDPATHS에는 예전 검사가 남긴 `bin/Verify;bin/Final`이 들어 있음 → 사용자가 직접 지우도록 안내함(보안 설정이라 Claude가 바꾸지 않음).

### 2026-10-21 오후 (리뷰 1~10 반영)

- 다른 작업이 중간까지 고치다 멈춘 코드를 이어받아 완료. 상세 표: `LegendAuto/VERIFICATION.md` 맨 위.
- 핵심 결함: 정의 비교가 ByLayer 투명도·선종류 TextAt 예외 때문에 거의 모든 정의를 '다름'으로 판정 → 수정. 잠긴 레이어 되돌리기 실패(131개 남음) → 수정.
- 새 시험: `tests/ReviewFixVerification.cs`(네이티브), `tests/concurrency-test.ps1`(두 프로세스), `tests/install-test.ps1`(설치), 화면 배율 모의(`PickerPreview.ScaledLayout`).
- 분석기 버전 `a6`(해치 패턴 선 추가) · 배포 `Template/preview-catalog.json` 다시 만듦.
- 설치본에서 보관용 파일(`legend-template.json`, `source-entities.json`) 제외. 배포 템플릿 이력: `deploy/shipped-template-hashes.txt`.

## 6. 남은 일 (우선순위 순)

0. 사용자 확인 대기: AutoCAD 재시작(NETLOAD 없이) → 우수 '삼각형' 기호가 보이는지, 도움말 홈·왼쪽 명령 목록이 보이는지. 그리고 TRUSTEDPATHS 정리.

1. **새 PC에서 보안 창 처리**: 새 컴퓨터마다 처음 켤 때 보안 창이 뜸. 선택지(보안 설정이므로 사용자 승인 필요):
   - 그대로 두고 "항상 로드" 안내(설명서·설치 완료 문구에 추가하면 좋음 — 아직 안 함)
   - 설치 시 TRUSTEDPATHS에 번들 폴더 추가
   - 코드 서명 인증서로 DLL 서명
   - (참고: `docs/PLAN-menu-ribbon.md`에 "ApplicationPlugins는 신뢰 위치라 경고 없음"이라고 적혀 있으나 실제로는 경고가 떴음 → 문서 수정 필요)
2. 사용자가 템플릿에 추가한 행('dsfsdf' 우수 23행)은 마지막 저장본(16:27)에서 지워져 있음. 다시 추가 후 `LGD`로 기호가 생성되는지 사용자 확인 필요(자동 테스트는 통과).
3. 리뷰 10번 추가 기능(제안만 함, 미구현): 범례 다시 만들기, 공종별 축척, 프리셋 내보내기/가져오기, 배치(종이 공간)에서 생성. 사용자 선택 대기.
4. 확인 안 된 것: 배치(종이 공간) 생성, 화면 배율 125·150·200%.
5. 알려진 제한: 복사 실패 시 빈 `Defpoints` 레이어가 남을 수 있음(AutoCAD가 지우지 못함, UNDO로 제거).
6. 다음 명령 추가 시: 새 폴더 + CadUi 창 + `AhjooengMenu.Register` 한 줄. 공통 기능(도움말 목록·프로그램 정보)을 Shell로 옮길지는 두 번째 명령이 생길 때 결정.
7. (권장) git 저장소로 관리(현재는 git 아님 — zip으로만 옮기는 중).

---

## 7. 명령 목록

| 명령 | 하는 일 | 메뉴 |
|---|---|---|
| `LGD` / `LEGEND` / `STORMLEGEND` | 범례 생성 창 | 범례 생성 (LGD) |
| `LEGENDHELP` | 범례 사용 설명서 열기 | — (도움말 목록에서) |
| `AHJOOENGHELP` | 도움말 홈(명령 목록) | 도움말 |
| `LEGENDALL` | 한 공종 전체 생성(명령줄) | — |
| `LEGENDORIGINAL` | 원본 표 그대로 복사 | — |
| `LEGENDTEMPLATE` | 템플릿 DWG 열기 | — (창의 [템플릿 수정]) |
| `LEGENDSETTINGS` | 설정 창 | — (창의 [설정]) |
| `AHJOOENGINFO` | 프로그램 정보 | 프로그램 정보 |
| `AHJOOENGMENU` | 메뉴 다시 만들기 | — |
| `AHJOOENGDIA` | FILEDIA·CMDDIA를 1로 | — |
