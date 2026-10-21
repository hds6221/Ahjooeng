using System;
using System.Windows.Forms;
using CadUi;

namespace StormLegend
{
    /// <summary>⚙ 설정: 범례를 도면에 넣는 방식. 저장하면 다음 생성부터 적용되고 다음 실행에도 유지됩니다.</summary>
    public sealed class SettingsForm : CadDialog
    {
        private readonly RadioButton exploded, block;
        private readonly CadNumberField gap;
        private readonly CheckBox rememberScale, select;
        private readonly RadioButton useDrawing, separate;
        public LegendSettings Result { get; private set; }

        public SettingsForm(LegendSettings current) : base("범례 설정", "범례를 도면에 넣는 방식을 정합니다. [저장]을 누르면 다음 생성부터 적용되고, 다음에 AutoCAD를 켜도 그대로 유지됩니다.")
        {
            var settings = (current ?? new LegendSettings()).Clone().Normalize();

            AddSection("① 생성 형태", "범례를 도면에 어떤 모양으로 넣을지 고릅니다.");
            exploded = AddChoice("OutputExploded", "개별 선·문자로 그리기 (추천)", "블록이 아니라서 글자·선을 바로 고칠 수 있습니다. 템플릿 원본과 같은 레이어로 들어갑니다.", !settings.OutputAsBlock);
            block = AddChoice("OutputBlock", "블록 하나로 묶기", "범례 전체가 한 덩어리라 옮기거나 지우기 쉽습니다. 내용을 고치려면 EXPLODE(분해)가 필요합니다.", settings.OutputAsBlock);

            AddSection("② 공종 표 사이 간격", "여러 공종(공사·우수…)을 가로로 늘어놓을 때 표와 표 사이를 얼마나 띄울지 정합니다.");
            gap = AddNumber("CategoryGap", "간격", (decimal)settings.CategoryGap, (decimal)LegendSettings.MinGap, (decimal)LegendSettings.MaxGap, "(1:100 기준, 0~5000, 기본 300)",
                "축척에 맞춰 함께 커집니다. 예: 300으로 두고 1:300으로 그리면 실제 간격은 900입니다. 0이면 표끼리 붙습니다.");

            AddSection("③ 시작 축척");
            rememberScale = AddCheck("RememberScale", "마지막에 쓴 축척으로 시작", "끄면 LEGEND를 열 때마다 1:100에서 시작합니다. 같은 축척으로 여러 번 그릴 때 켜 두면 편합니다.", settings.RememberScale);

            AddSection("④ 생성한 뒤");
            select = AddCheck("SelectAfterCreate", "만든 범례를 선택된 상태로 두기", "생성 직후 바로 MOVE(M)·COPY로 옮길 수 있습니다. 끄면 아무것도 선택하지 않습니다.", settings.SelectAfterCreate);

            AddSection("⑤ 도면에 같은 이름의 정의가 있을 때", "작업 도면에 템플릿과 이름은 같지만 색·글꼴·선종류 등이 다른 레이어·문자 스타일·선종류·블록이 있을 때입니다. 어느 쪽이든 기존 도면의 정의는 바꾸지 않습니다.");
            useDrawing = AddChoice("ConflictUseDrawing", "도면 정의 그대로 쓰기 (추천)", "레이어가 늘어나지 않습니다. 대신 범례 모양이 템플릿과 다를 수 있어 생성 후 명령줄에 무엇이 다른지 알려줍니다.", settings.Conflicts == DefinitionConflict.UseDrawing);
            separate = AddChoice("ConflictSeparate", "범례 전용 이름으로 따로 넣기", "다른 것만 '이름_범례'(예: C-우수-범례-SO_범례)로 따로 넣어 템플릿과 똑같이 보이게 합니다.", settings.Conflicts == DefinitionConflict.Separate);

            AddButton("SaveSettings", "저장", true, (s, e) => Save(settings), false, CadGlyph.Save);
            AddButton("CancelSettings", "취소", false, (s, e) => { DialogResult = DialogResult.Cancel; Close(); });
            AddButton("ResetSettings", "기본값으로", false, (s, e) => Reset(), true, CadGlyph.Reset);
            CancelButton = (IButtonControl)Controls.Find("CancelSettings", true)[0];
            FinishLayout();
        }

        private void Reset()
        {
            var d = new LegendSettings();
            exploded.Checked = !d.OutputAsBlock; block.Checked = d.OutputAsBlock; gap.Value = (decimal)d.CategoryGap;
            rememberScale.Checked = d.RememberScale; select.Checked = d.SelectAfterCreate;
            useDrawing.Checked = d.Conflicts == DefinitionConflict.UseDrawing; separate.Checked = d.Conflicts == DefinitionConflict.Separate;
        }

        private void Save(LegendSettings settings)
        {
            if (!gap.IsValid)
            {
                MessageBox.Show(this, "공종 표 사이 간격은 0~5000 사이의 숫자로 입력하세요.", "범례 설정", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                gap.Focus(); gap.SelectAll(); return;
            }
            settings.OutputAsBlock = block.Checked; settings.CategoryGap = (double)gap.Value;
            settings.RememberScale = rememberScale.Checked; settings.SelectAfterCreate = select.Checked;
            settings.Conflicts = separate.Checked ? DefinitionConflict.Separate : DefinitionConflict.UseDrawing;
            Result = settings.Normalize(); DialogResult = DialogResult.OK; Close();
        }
    }
}
