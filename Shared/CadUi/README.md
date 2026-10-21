# CadUi — 명령 공용 화면 템플릿

모든 Autocad_auto 명령 창이 같은 모양(UI 디자인 시안 "03 AUTOCAD · 오토캐드 스타일")을 쓰도록 만든 WinForms 소스 묶음입니다. 기준은 LEGEND 창입니다(`LegendAuto/src/StormLegend/MultiLegendForm.cs`).

```
┌ 탭 줄 (공종·종류 고르기, 숫자 배지) ───────────────────────────┐
│ 축척 [1:100 ▾]  [📄][📂][💾]  [✕][↑][↓]  [기타 아이콘…]          │  ← 도구 막대
│ ┌ 원본 항목 ──┐ ┌ 선택 항목 (n) ─┐ ┌ CAD 미리보기 ─[−][+][⤢]┐ │  ← 묶음 상자(CadGroupPanel)
│ │ 검색…   🔍 │ │ 기호 명칭 규격 수량│ │      흰 종이           │ │
│ └───────────┘ └──────────────┘ └──────────────────┘ │
│ [▦ 템플릿 수정] [⚙ 설정]   요약(굵게) / 안내(작게)    [📄 생성] │  ← 아래 줄
└────────────────────────────────────────────────┘
```

기본 크기 1120×585(최소 900×480), 열 때마다 AutoCAD 화면 정가운데, 글꼴 맑은 고딕 9pt, 행 높이 26.

## 새 명령에서 쓰는 법

1. `Autocad_auto/새명령/src/새명령/새명령.csproj`에 소스를 링크합니다. DLL로 따로 나누지 않습니다. 명령마다 자기 DLL 안에 들어가므로, 여러 명령을 함께 NETLOAD해도 버전이 충돌하지 않습니다.

```xml
<Compile Include="..\..\..\Shared\CadUi\*.cs" Link="CadUi\%(Filename)%(Extension)" />
<Reference Include="System.Windows.Forms" /><Reference Include="System.Drawing" />
```

2. 창은 `CadToolWindow`를 상속합니다.

```csharp
using CadUi;

public sealed class MyForm : CadToolWindow
{
    public MyForm() : base("새 명령", withTabs: true)
    {
        Tabs.Add("a", "종류A"); Tabs.Add("b", "종류B"); Tabs.TabClicked += (s, key) => Tabs.Select(key); Tabs.Select("a");

        AddToolbarLabel("축척");
        AddToolbarButton(CadGlyph.New,  "Clear", "비우기",      (s, e) => { });
        AddToolbarButton(CadGlyph.Open, "Open",  "불러오기",    (s, e) => { });
        AddToolbarButton(CadGlyph.Save, "Save",  "저장",        (s, e) => { });
        AddToolbarGap();
        AddToolbarButton(CadGlyph.Delete, "Remove", "빼기",     (s, e) => { });

        var list = new CadGroupPanel("원본 항목") { Dock = DockStyle.Left, Width = 280 };
        list.Content.Controls.Add(new CadSearchBox("항목 검색…") { Dock = DockStyle.Top });
        Body.Controls.Add(list);

        AddFooterButton(CadGlyph.Settings, "Settings", "설정", "설정 메뉴", (s, e) => { }, primary: false);
        AddFooterButton(CadGlyph.Generate, "Run", "생성", "도면에 그립니다", (s, e) => { DialogResult = DialogResult.OK; Close(); }, primary: true);

        FinishLayout();               // 마지막에 한 번: 색 입히기
        Summary.Text = "고른 항목 0개"; SetStatus("왼쪽에서 항목을 클릭하세요");
    }
}
```

3. AutoCAD에서는 `Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(form)`으로 엽니다. 이 호출은 `[MethodImpl(NoInlining)]` 메서드 안에 두어야 AutoCAD Core Console 검증에서도 명령이 돌아갑니다(LegendAuto `Commands.ShowPicker` 참고).

## 구성 요소

| 파일 | 내용 |
|---|---|
| `CadTheme.cs` | 색(창·상자·선택·주 버튼 등), 글꼴, `Apply`(전체 색 입히기), `StyleGrid`(표), `SetPlaceholder`(흐린 안내 글자), `SingleRow`(한 줄 배치 넘침 방지) |
| `CadControls.cs` (입력칸) | `CadTextField`: 네 면 테두리가 온전한 입력칸(오른쪽에 검색 돋보기·▾ 목록 선택 가능), `CadSearchBox`, `CadNumberField`(숫자만·범위 밖이면 빨간 테두리). 기본 ComboBox·NumericUpDown 대신 이것을 씁니다 |
| `CadControls.cs` | `CadGlyph` 아이콘(직접 그림, 이미지 파일 없음), `CadIconButton`, `CadTabStrip`, `CadGroupPanel`, `CadSearchBox`, `CadMenu`(어두운 팝업 메뉴) |
| `CadDialog.cs` | 설정 창 틀: 굵은 제목 + 쉬운 설명 + 선택 단추/체크/숫자 칸, [기본값으로] [취소] [저장] (예: LegendAuto `SettingsForm`) |
| `CadToolWindow.cs` | 공통 창 틀: 탭 줄 · 도구 막대 · 본문 · 아래 줄(보조 버튼 / 요약·안내 / 주 버튼), Esc로 닫기 |

## 지킬 것

- 화면 글자는 쉬운 한국어로 씁니다. 도구 막대 버튼은 아이콘과 **글자를 함께** 쓰고(`AddToolbarButton(..., text)`), 고를 것이 있으면 `ShowsMenu = true`(▾)로 메뉴를 엽니다. 모든 버튼에는 툴팁으로 하는 일을 적습니다.
- 선택은 `CadTheme.Selected`(차분한 강철 파랑)만 씁니다. 원색 파랑은 쓰지 않습니다.
- 표 갱신은 클릭 처리 중에 하지 말고 `BeginInvoke`로 미룹니다. 그렇지 않으면 DataGridView "SetCurrentCellAddressCore 재진입" 오류가 납니다(`CategoryPanel.OnModelChanged` 참고).
- 한 줄짜리 `TableLayoutPanel`에는 `CadTheme.SingleRow`를 씁니다. 행 스타일이 없으면 버튼이 칸 밖으로 넘칩니다.
- 플러그인은 `FILEDIA`·`CMDDIA`를 0으로 바꾸지 않습니다(LegendAuto `DialogSettings` 참고).
