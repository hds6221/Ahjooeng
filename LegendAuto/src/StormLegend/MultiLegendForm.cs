using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using CadUi;

namespace StormLegend
{
    /// <summary>
    /// LEGEND 창(공용 CadUi 템플릿). 탭으로 공종 → 왼쪽 항목 클릭 → 축척 → [범례 생성].
    /// 공종 화면은 처음 열 때 만들고, 선택 상태는 공종을 바꿔도 유지됩니다.
    /// </summary>
    public sealed class MultiLegendForm : CadToolWindow
    {
        private static readonly string[] CommonScales = { "1:50", "1:100", "1:200", "1:250", "1:300", "1:500", "1:600", "1:1000", "1:1200" };

        private readonly CatalogIndex index;
        private readonly PreviewCatalog catalog;
        private readonly CadTextField scaleInput;
        private readonly CadIconButton generate, clearButton, openButton, saveButton, removeButton, upButton, downButton, blankButton;
        private readonly string settingsPath;
        private LegendSettings settings;
        private readonly Dictionary<string, CategoryState> states = new Dictionary<string, CategoryState>();
        private readonly Dictionary<string, CategoryPanel> panels = new Dictionary<string, CategoryPanel>();

        public IList<LegendBatchItem> BatchSelection { get; private set; }
        public int ScaleDenominator { get; private set; } = 100;
        public CategoryPanel ActivePicker { get; private set; }
        public string ActiveCategoryId { get; private set; }
        /// <summary>사용자가 [템플릿 수정]을 눌러 창을 닫았는지 여부.</summary>
        public bool EditTemplateRequested { get; private set; }

        /// <summary>현재 설정(⚙ 설정에서 바꾸면 저장 성공 여부와 상관없이 이번 실행에 적용).</summary>
        public LegendSettings Settings { get { return settings; } }
        /// <summary>[범례 생성]을 누른 순간의 설정 사본. 명령은 디스크를 다시 읽지 않고 이 값으로 그립니다.</summary>
        public LegendSettings ConfirmedSettings { get; private set; }

        public MultiLegendForm(CatalogIndex index, string presetDirectory = null, string notice = null, string settingsPath = null) : base("범례 생성", true)
        {
            if (index == null) throw new ArgumentNullException("index");
            this.index = index;
            this.settingsPath = settingsPath ?? (presetDirectory != null ? Path.Combine(presetDirectory, "settings.json") : LegendSettings.DefaultPath);
            settings = LegendSettings.Load(this.settingsPath);
            try { catalog = PreviewCatalog.LoadCached(index.PreviewPath, index.Manifests); }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is System.Runtime.Serialization.SerializationException || ex is UnauthorizedAccessException)
            {
                // 미리보기가 망가져도 항목 선택·생성은 할 수 있게 합니다(그림만 비활성).
                catalog = PreviewCatalog.Unavailable("미리보기 데이터를 읽지 못했습니다: " + ex.Message);
            }

            foreach (var entry in index.Categories) Tabs.Add(entry.Id, entry.Name);
            Tabs.AccessibleName = "공종 선택 (Ctrl+1~7 또는 방향키)";
            Tabs.TabClicked += (s, id) => SelectCategory(id);

            // ── 도구 막대: 꼭 필요한 기능만, 글자와 함께
            AddToolbarLabel("축척");
            scaleInput = new CadTextField(null, CadGlyph.Dropdown) { Name = "ScaleDenominator", Width = 110, Height = CadTheme.ToolButton, Margin = new Padding(0, 0, 12, 0) };
            scaleInput.Choices.AddRange(CommonScales); scaleInput.Text = "1:" + settings.StartScale;
            Tips.SetToolTip(scaleInput, "도면 축척. 목록에서 고르거나 직접 입력합니다(예: 1:300 또는 300).\r\n1:100이 기본 크기이고 1:300이면 3배로 그립니다.");
            Toolbar.Controls.Add(scaleInput);
            openButton = AddToolbarButton(CadGlyph.Open, "OpenPreset", "저장해 둔 구성(프리셋)이나 '전체 항목'을 불러와 선택 목록을 한 번에 채웁니다.", (s, e) => Active(p => p.ShowPresetMenu(openButton)), "불러오기");
            openButton.ShowsMenu = true; openButton.Width += 14;
            saveButton = AddToolbarButton(CadGlyph.Save, "SaveDefault", "지금 선택한 항목·고친 명칭·수량·빈 행을 이름 붙여 저장합니다.\r\n다음에 LEGEND를 열면 이 구성으로 시작합니다.", (s, e) => Active(p => p.SavePreset()), "저장");
            clearButton = AddToolbarButton(CadGlyph.Trash, "ClearSelection", "선택한 항목을 비웁니다. 이 공종만 또는 모든 공종을 고를 수 있습니다.", (s, e) => ShowClearMenu(), "비우기");
            clearButton.ShowsMenu = true; clearButton.Width += 14;
            AddToolbarGap();
            removeButton = AddToolbarButton(CadGlyph.Delete, "RemoveRow", "가운데 표에서 고른 행을 뺍니다. (Delete 키)", (s, e) => Active(p => p.RemoveCurrent()), "빼기");
            upButton = AddToolbarButton(CadGlyph.Up, "MoveUp", "가운데 표에서 고른 행을 한 칸 위로 옮깁니다. 범례도 이 순서로 그려집니다.", (s, e) => Active(p => p.MoveCurrent(-1)), "위로");
            downButton = AddToolbarButton(CadGlyph.Down, "MoveDown", "가운데 표에서 고른 행을 한 칸 아래로 옮깁니다.", (s, e) => Active(p => p.MoveCurrent(1)), "아래로");
            AddToolbarGap();
            blankButton = AddToolbarButton(CadGlyph.InsertBelow, "AddBlankRow", "공종 없이 빈 칸만 있는 행을 넣습니다(손으로 적을 칸 등).\r\n고른 행 바로 아래(표 중간) 또는 맨 아래를 고릅니다.", (s, e) => ShowBlankMenu(), "빈 행 추가");
            blankButton.ShowsMenu = true; blankButton.Width += 14;

            // ── 아래 버튼
            AddFooterButton(CadGlyph.Template, "EditTemplate", "템플릿 수정", "범례템플릿.dwg를 열어 공종 표에 항목을 추가·수정합니다.\r\n저장한 뒤 LEGEND를 다시 실행하면 새 항목이 목록에 나타납니다.", (s, e) => RequestTemplateEdit(), false);
            AddFooterButton(CadGlyph.Settings, "Settings", "설정", "생성 형태(개별 선·문자 / 블록), 공종 표 사이 간격, 시작 축척, 생성 후 선택 여부를 정합니다.", (s, e) => OpenSettings(), false);
            AddFooterButton(CadGlyph.Help, "Help", "사용 방법", "처음 쓰는 분을 위한 순서 안내", (s, e) => ShowHelp(), false);
            generate = AddFooterButton(CadGlyph.Generate, "GenerateBatch", "범례 생성", "창이 닫히면 도면에서 범례의 왼쪽 위 위치를 클릭하세요.", (s, e) => Confirm(), true);

            foreach (var entry in index.Categories)
            {
                var manifest = index.LoadManifest(entry);
                string path = presetDirectory == null ? null : Path.Combine(presetDirectory, entry.Id + ".json");
                var state = new CategoryState(manifest, path, entry.Id == index.DefaultCategoryId);
                states.Add(entry.Id, state); state.Model.Changed += (s, e) => UpdateSummary();
            }

            Status.Cursor = System.Windows.Forms.Cursors.Hand;
            Tips.SetToolTip(Status, "클릭하면 안내·오류 내용을 모두 확인하고 복사할 수 있습니다.");
            Status.Click += (s, e) => ShowStatusDetails();
            FinishLayout();
            scaleInput.TextChanged += (s, e) => UpdateSummary();
            SelectCategory(index.DefaultCategoryId);
            var notes = new[] { notice, catalog.UnavailableReason == null ? null : "⚠ " + catalog.UnavailableReason + " — 항목 선택·생성은 그대로 할 수 있습니다." }
                .Concat(states.Values.Select(st => st.LoadMessage)).Where(t => !string.IsNullOrEmpty(t)).ToList();
            if (notes.Count > 0) SetStatus(string.Join("  /  ", notes), notes.Any(n => n.Contains("못") || n.StartsWith("⚠")));
            else SetStatus("왼쪽 항목을 클릭해 넣고, 가운데 칸을 클릭해 명칭·규격·수량을 고치세요. 공종을 바꿔도 고른 내용은 남습니다.");
            UpdateSummary();
            FormClosing += (s, e) => { if (DialogResult != DialogResult.OK) DiscardEdits(); };
            Shown += (s, e) => ShowPresetIssues();
        }

        /// <summary>템플릿이 바뀌어 다시 연결하지 못한 프리셋 항목을 이름과 이유까지 한 번 보여줍니다.</summary>
        public IList<string> PresetIssueLines
        {
            get { return index.Categories.SelectMany(c => states[c.Id].Issues.Select(i => c.Name + " · " + i)).ToList(); }
        }

        private void ShowStatusDetails()
        {
            using (var detail = new System.Windows.Forms.Form { Text = "안내 상세", Width = 680, Height = 340, StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false })
            {
                detail.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
                    Text = Status.Text, Font = CadTheme.Ui, BackColor = CadTheme.Field, ForeColor = CadTheme.Text });
                detail.ShowDialog(this);
            }
        }

        private void ShowPresetIssues()
        {
            var lines = PresetIssueLines;
            if (lines.Count == 0 || Opacity == 0) return;
            MessageBox.Show(this, "템플릿이 바뀌어 저장된 프리셋의 일부 항목을 확실히 찾지 못했습니다.\r\n잘못된 기호에 수량·문구가 붙지 않도록 이 항목은 빼고 열었습니다. 다시 골라 [저장]하세요.\r\n\r\n• "
                + string.Join("\r\n• ", lines.Take(15)) + (lines.Count > 15 ? "\r\n… 외 " + (lines.Count - 15) + "건" : ""),
                "프리셋 다시 선택 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public IEnumerable<CategoryState> States { get { return index.Categories.Select(c => states[c.Id]); } }
        public PickerModel ModelOf(string categoryId) { return states[categoryId].Model; }

        private void Active(Action<CategoryPanel> action) { if (ActivePicker != null) action(ActivePicker); }

        public void SelectCategory(string id)
        {
            if (ActiveCategoryId == id || !states.ContainsKey(id)) return;
            if (ActivePicker != null && !ActivePicker.CommitEdits()) return;
            CategoryPanel panel;
            if (!panels.TryGetValue(id, out panel))
            {
                panel = new CategoryPanel(states[id], catalog) { Visible = false };
                panel.Status += (s, e) => SetStatus(e.Text, e.IsError);
                panel.StateChanged += (s, e) => { if (s == ActivePicker) UpdateToolbar(); };
                panels.Add(id, panel); Body.Controls.Add(panel);
            }
            if (ActivePicker != null) ActivePicker.Hide();
            ActiveCategoryId = id; ActivePicker = panel; panel.Show(); panel.BringToFront(); Tabs.Select(id);
            UpdateSummary(); UpdateToolbar();
        }

        public void ClearAll()
        {
            if (ActivePicker != null && !ActivePicker.CommitEdits()) return;
            foreach (var state in states.Values) state.Model.Clear();
            SetStatus("모든 공종의 선택을 비웠습니다");
        }

        private void UpdateToolbar()
        {
            var p = ActivePicker;
            removeButton.Enabled = p != null && p.CanRemove; upButton.Enabled = p != null && p.CanMove(-1); downButton.Enabled = p != null && p.CanMove(1);
            blankButton.Enabled = p != null && p.CanAddBlank; saveButton.Enabled = p != null && p.Model.RowCount > 0;
            clearButton.Enabled = states.Values.Any(st => st.Model.RowCount > 0);
        }

        private void ShowClearMenu()
        {
            var menu = CadMenu.Create();
            CadMenu.Add(menu, (ActivePicker != null ? ActivePicker.Manifest.CategoryName : "이 공종") + "만 비우기", (s, e) => Active(p => p.ClearCategory()), ActivePicker != null && ActivePicker.Model.RowCount > 0);
            CadMenu.Add(menu, "모든 공종 비우기", (s, e) => ClearAll());
            menu.Closed += (s, e) => { if (!IsDisposed && !Disposing && IsHandleCreated) BeginInvoke((Action)(() => menu.Dispose())); else menu.Dispose(); };
            CadMenu.Show(menu, clearButton);
        }

        private void ShowBlankMenu()
        {
            if (ActivePicker == null) return;
            var menu = CadMenu.Create();
            CadMenu.AddHeader(menu, "공종 없이 빈 칸만 있는 행 넣기");
            CadMenu.Add(menu, "고른 행 바로 아래 (표 중간)", (s, e) => Active(p => p.AddBlankAfter()), ActivePicker.CanAddBlankAfter);
            CadMenu.Add(menu, "맨 아래", (s, e) => Active(p => p.AddBlankEnd()));
            if (!ActivePicker.CanAddBlankAfter) CadMenu.AddHeader(menu, "※ 표 중간에 넣으려면 먼저 가운데 표에서 행을 클릭하세요");
            menu.Closed += (s, e) => { if (!IsDisposed && !Disposing && IsHandleCreated) BeginInvoke((Action)(() => menu.Dispose())); else menu.Dispose(); };
            CadMenu.Show(menu, blankButton);
        }

        public void OpenSettings()
        {
            using (var dialog = new SettingsForm(settings))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                ApplySettings(dialog.Result);
            }
        }

        /// <summary>설정을 바꾸고 저장합니다(설정 창·검사용).</summary>
        public void ApplySettings(LegendSettings next)
        {
            settings = next.Clone().Normalize();
            try { settings.Save(settingsPath); SetStatus("설정을 저장했습니다 · " + Describe(settings)); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            { SetStatus("설정 파일을 저장하지 못했습니다(" + ex.Message + "). 이번 생성에는 바꾼 설정을 그대로 적용합니다 · " + Describe(settings), true); }
            UpdateSummary();
        }

        private static string Describe(LegendSettings s)
        {
            return (s.OutputAsBlock ? "블록 하나로 묶기" : "개별 선·문자") + " · 공종 간격 " + s.CategoryGap.ToString("0", CultureInfo.InvariantCulture) + (s.RememberScale ? " · 마지막 축척 기억" : "");
        }

        /// <summary>그림이 있는 사용 설명서(Help\index.html)를 엽니다. 없으면 글로 된 안내를 보여줍니다.</summary>
        private void ShowHelp()
        {
            string help = Path.Combine(Path.GetDirectoryName(typeof(MultiLegendForm).Assembly.Location), "Help", "index.html");
            if (File.Exists(help))
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(help) { UseShellExecute = true }); SetStatus("사용 설명서를 열었습니다(웹 브라우저)."); return; }
                catch (System.ComponentModel.Win32Exception) { }
            }
            MessageBox.Show(this,
                "① 위쪽 탭에서 공종(공사·우수…)을 고릅니다.\r\n" +
                "② 왼쪽 '원본 항목'을 클릭해 넣습니다. 다시 누르면 빠집니다.\r\n" +
                "③ 가운데 표의 명칭·규격·수량 칸을 클릭해 고칩니다(Enter로 확정).\r\n" +
                "④ 축척을 고르고 [범례 생성] → 도면에서 범례 왼쪽 위 위치를 클릭합니다.\r\n\r\n" +
                "도구 막대\r\n" +
                "  불러오기 · 저장 : 자주 쓰는 구성을 저장하고 다시 불러옵니다.\r\n" +
                "  비우기 : 이 공종 또는 모든 공종의 선택을 비웁니다.\r\n" +
                "  빼기 · 위로 · 아래로 : 가운데 표에서 고른 행을 빼거나 순서를 바꿉니다.\r\n" +
                "  빈 행 추가 : 공종 없이 빈 칸만 있는 행을 표 중간이나 맨 아래에 넣습니다.\r\n" +
                "  가운데 표에서 마우스 오른쪽 버튼을 누르면 같은 기능과 '원래 이름으로'가 있습니다.\r\n\r\n" +
                "단축키: Ctrl+1~7 공종 이동 · Delete 행 빼기 · Esc 닫기",
                "사용 방법", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>"1:300", "1 : 300", "300" 모두 받습니다.</summary>
        public static bool TryParseScale(string text, out int denominator)
        {
            denominator = 0;
            var match = Regex.Match(text ?? "", @"^\s*(?:1\s*:\s*)?(\d{1,6})\s*$");
            return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out denominator)
                && denominator >= LegendComposer.MinScale && denominator <= LegendComposer.MaxScale;
        }

        private void UpdateSummary()
        {
            int categories = 0, items = 0, blanks = 0;
            foreach (var entry in index.Categories)
            {
                var model = states.ContainsKey(entry.Id) ? states[entry.Id].Model : null;
                int count = model == null ? 0 : model.Count, blank = model == null ? 0 : model.BlankCount;
                items += count; blanks += blank; if (count + blank > 0) categories++;
                Tabs.SetBadge(entry.Id, count);
            }
            int denominator; bool valid = TryParseScale(scaleInput.Text, out denominator);
            string factor = valid ? (denominator / 100.0).ToString("0.##", CultureInfo.InvariantCulture) : null;
            Summary.Text = !valid ? "축척을 1:100 또는 300처럼 입력하세요 (1~100000)"
                : items + blanks == 0 ? "아직 고른 항목이 없습니다"
                : categories + "개 공종 · " + items + "개 항목" + (blanks > 0 ? " + 빈 행 " + blanks : "") + " · 1:" + denominator + " (" + factor + "배)";
            Summary.ForeColor = valid ? CadTheme.Text : CadTheme.Error;
            generate.Enabled = valid && items + blanks > 0;
            UpdateToolbar();
        }

        private void Confirm()
        {
            if (ActivePicker != null && !ActivePicker.CommitEdits()) return;
            int denominator; if (!TryParseScale(scaleInput.Text, out denominator)) { scaleInput.Focus(); scaleInput.SelectAll(); return; }
            var selected = index.Categories.Where(c => states[c.Id].Model.RowCount > 0)
                .Select(c => new LegendBatchItem(states[c.Id].Manifest, states[c.Id].Model.Selection, index.SourcePath(states[c.Id].Manifest))).ToList();
            if (selected.Count == 0) return;
            BatchSelection = selected; ScaleDenominator = denominator; ConfirmedSettings = settings.Clone().Normalize();
            if (settings.RememberScale && settings.LastScale != denominator)
            {
                settings.LastScale = denominator;
                try { settings.Save(settingsPath); } catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            }
            DialogResult = DialogResult.OK; Close();
        }

        private void RequestTemplateEdit()
        {
            var answer = MessageBox.Show(this,
                "범례 템플릿(" + TemplateRepository.TemplateFileName + ")을 AutoCAD에서 엽니다.\r\n\r\n" +
                "• 이 창은 닫힙니다. 지금 고른 내용을 다음에도 쓰려면 먼저 프리셋 저장을 누르세요.\r\n" +
                "• 템플릿을 고친 뒤 저장(Ctrl+S)하고, 작업 도면에서 LEGEND를 다시 실행하면 반영됩니다.\r\n\r\n계속할까요?",
                "템플릿 수정", MessageBoxButtons.OKCancel, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1);
            if (answer != DialogResult.OK) return;
            DiscardEdits(); EditTemplateRequested = true; DialogResult = DialogResult.Cancel; Close();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Ctrl+1~9: 해당 순서의 공종으로 이동합니다.
            if ((keyData & Keys.Control) == Keys.Control)
            {
                int number = (int)(keyData & Keys.KeyCode) - (int)Keys.D1;
                if (number >= 0 && number < Math.Min(9, index.Categories.Count)) { SelectCategory(index.Categories[number].Id); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnCancel() { DiscardEdits(); }
        private void DiscardEdits() { AutoValidate = AutoValidate.Disable; foreach (var panel in panels.Values) panel.CancelEdits(); }
    }
}
