using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using CadUi;

namespace StormLegend
{
    /// <summary>한 공종의 선택 상태(화면을 만들기 전에도 개수를 셀 수 있도록 분리).</summary>
    public sealed class CategoryState
    {
        public TemplateManifest Manifest { get; private set; }
        public PickerModel Model { get; private set; }
        public PresetStore Store { get; private set; }
        public PresetDocument Saved { get; private set; }
        public bool HasSavedDefault { get; private set; }
        /// <summary>프리셋을 읽다 생긴 안내. 없으면 null.</summary>
        public string LoadMessage { get; private set; }

        public CategoryState(TemplateManifest manifest, string presetPath, bool selectAllWhenNoPreset)
        {
            Manifest = manifest; Store = new PresetStore(manifest, presetPath); Model = new PickerModel(manifest, false);
            try
            {
                Saved = Store.Load();
                var preset = Saved.Presets.FirstOrDefault(p => p.Id == Saved.DefaultPresetId);
                if (preset != null) { Model.Apply(preset.Items); HasSavedDefault = true; }
                if (Saved.Issues.Count > 0) LoadMessage = manifest.CategoryName + ": 프리셋 항목 " + Saved.Issues.Count + "개를 현재 템플릿에 다시 연결하지 못해 뺐습니다(다시 골라 저장하세요).";
                else if (Saved.RelinkedItems > 0) LoadMessage = manifest.CategoryName + ": 템플릿이 바뀌어 프리셋 항목 " + Saved.RelinkedItems + "개를 같은 원본 항목으로 다시 연결했습니다.";
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is System.IO.InvalidDataException || ex is System.Runtime.Serialization.SerializationException || ex is ArgumentException || ex is UnauthorizedAccessException)
            {
                Saved = new PresetDocument { CategoryId = manifest.CategoryId, SourceSha256 = manifest.SourceSha256 };
                LoadMessage = manifest.CategoryName + ": 저장된 프리셋을 읽지 못했습니다 — " + ex.Message;
            }
            if (!HasSavedDefault && selectAllWhenNoPreset && !Saved.StartEmpty) Model.SelectAll();
        }

        public void ReloadSaved() { Saved = Store.Load(); HasSavedDefault = Saved.Presets.Any(p => p.Id == Saved.DefaultPresetId); }

        /// <summary>다시 연결하지 못한 프리셋 항목(이름과 이유).</summary>
        public IList<PresetIssue> Issues { get { return Saved == null ? new PresetIssue[0] : Saved.Issues; } }

        /// <summary>기본 제공 프리셋 + 저장한 프리셋.</summary>
        public IList<LegendPreset> Presets
        {
            get
            {
                var list = new List<LegendPreset>
                {
                    new LegendPreset { Id = "builtin-all", Name = "전체 항목 (" + Manifest.Rows.Count + "개)", Items = Manifest.Rows.Select(r => new PresetItem { ItemId = r.Id, Quantity = "-" }).ToList() },
                    new LegendPreset { Id = "builtin-basic", Name = "앞쪽 " + Math.Min(6, Manifest.Rows.Count) + "개만 (예시)", Items = Manifest.Rows.Take(6).Select(r => new PresetItem { ItemId = r.Id, Quantity = "-" }).ToList() },
                };
                list.AddRange(Saved.Presets);
                return list;
            }
        }
    }

    public sealed class StatusEventArgs : EventArgs
    {
        public string Text { get; private set; }
        public bool IsError { get; private set; }
        public StatusEventArgs(string text, bool isError) { Text = text; IsError = isError; }
    }

    /// <summary>
    /// 한 공종 화면: [원본 항목] [선택 항목 (n)] [CAD 미리보기]. 도구 막대 버튼은 창(MultiLegendForm)에 있고 이 화면의 명령을 부릅니다.
    /// </summary>
    public sealed class CategoryPanel : UserControl
    {
        private readonly CategoryState state;
        private readonly TemplateManifest manifest;
        private readonly PreviewCatalog catalog;
        private readonly FlowLayoutPanel cards;
        private readonly TextBox search;
        private readonly DataGridView grid;
        private readonly CadGroupPanel selectedBox;
        private readonly LegendPreviewPanel preview;
        private readonly ToolTip tips = new ToolTip { AutoPopDelay = 12000, InitialDelay = 400 };
        private readonly Dictionary<string, FacilityCard> cardById = new Dictionary<string, FacilityCard>();
        private readonly Dictionary<string, Bitmap> symbols = new Dictionary<string, Bitmap>();
        private readonly Bitmap noSymbol = new Bitmap(1, 1);
        private readonly SymbolZoom zoom;
        private bool refreshing, editing, refreshPending;
        private string pendingSelect;

        public event EventHandler<StatusEventArgs> Status;
        /// <summary>선택 목록이나 현재 행이 바뀌면 알립니다(도구 막대 버튼 상태 갱신용).</summary>
        public event EventHandler StateChanged;
        public PickerModel Model { get { return state.Model; } }
        public TemplateManifest Manifest { get { return manifest; } }
        public CategoryState State { get { return state; } }

        public CategoryPanel(CategoryState state, PreviewCatalog catalog)
        {
            this.state = state; this.catalog = catalog; manifest = state.Manifest;
            Font = CadTheme.Ui; Dock = DockStyle.Fill; AutoScaleMode = AutoScaleMode.Dpi; Padding = Padding.Empty;
            bool editable = manifest.HasQuantityColumn;
            zoom = new SymbolZoom(catalog);

            var columns = CadTheme.SingleRow(new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty });
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));

            // ── 원본 항목
            var source = new CadGroupPanel(editable ? "원본 항목 (" + manifest.Rows.Count + ")" : "원본 항목 · 한 세트") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 6, 0) };
            var searchBox = new CadSearchBox("항목 검색…") { Dock = DockStyle.Top }; search = searchBox.Box; search.Name = "SearchBox";
            var header = new ItemListHeader { Dock = DockStyle.Top };
            cards = new FlowLayoutPanel { Name = "Cards", Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false, FlowDirection = FlowDirection.TopDown, Margin = Padding.Empty, BackColor = CadTheme.Field };
            CadTheme.Keep(cards);
            cards.SuspendLayout();
            foreach (var row in manifest.Rows)
            {
                var card = new FacilityCard(row, catalog);
                card.Click += (s, e) => { if (CommitEdits()) Model.Toggle(((FacilityCard)s).Row.Id); };
                // 마우스를 올리면 기호를 크게 보여 줍니다(명칭·규격·단위·비고 포함).
                card.MouseEnter += (s, e) => { var c = (FacilityCard)s; zoom.Hover(c, c.ClientRectangle, c.Row, "클릭하면 넣고, 다시 누르면 뺍니다."); };
                card.MouseLeave += (s, e) => zoom.Hide();
                cards.Controls.Add(card); cardById.Add(row.Id, card);
            }
            cards.ResumeLayout();
            var gap = new Panel { Dock = DockStyle.Top, Height = 6 };
            source.Content.Controls.Add(cards); source.Content.Controls.Add(header); source.Content.Controls.Add(gap); source.Content.Controls.Add(searchBox);
            cards.Resize += (s, e) => ResizeCards(); search.TextChanged += (s, e) => FilterCards();
            cards.Scroll += (s, e) => zoom.Hide(); cards.MouseWheel += (s, e) => zoom.Hide();

            // ── 선택 항목
            selectedBox = new CadGroupPanel("선택 항목") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 6, 0) };
            grid = new DataGridView { Name = "SelectedItems", Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, AllowUserToOrderColumns = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
                EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2, StandardTab = true };
            grid.Columns.Add(new DataGridViewImageColumn { Name = "Symbol", HeaderText = "", ReadOnly = true, FillWeight = 46, MinimumWidth = 84, ImageLayout = DataGridViewImageCellLayout.Normal, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Facility", HeaderText = "명칭", MaxInputLength = LegendSelection.MaxLabelLength, FillWeight = 92, SortMode = DataGridViewColumnSortMode.NotSortable, ReadOnly = !editable });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Spec", HeaderText = "규격", MaxInputLength = LegendSelection.MaxLabelLength, FillWeight = 70, SortMode = DataGridViewColumnSortMode.NotSortable, ReadOnly = !editable });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Quantity", HeaderText = "수량", FillWeight = 46, MaxInputLength = 16, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns["Quantity"].Visible = manifest.HasQuantityColumn;
            ((DataGridViewImageColumn)grid.Columns["Symbol"]).DefaultCellStyle.NullValue = null;
            grid.CellValidating += ValidateCell; grid.CellEndEdit += CommitCell;
            grid.CurrentCellChanged += (s, e) => { QueueUi(RaiseState); };
            grid.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete && !grid.IsCurrentCellInEditMode) { RemoveCurrent(); e.Handled = true; } };
            grid.CellClick += (s, e) => { if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && !grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ReadOnly && !grid.IsCurrentCellInEditMode) grid.BeginEdit(true); };
            grid.CellMouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Right || e.RowIndex < 0) return;
                try { grid.CurrentCell = grid.Rows[e.RowIndex].Cells[Math.Max(1, e.ColumnIndex)]; } catch (InvalidOperationException) { }
            };
            grid.MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) ShowRowMenu(e.Location); };
            grid.CellMouseEnter += (s, e) => HoverSymbol(e.RowIndex, e.ColumnIndex);
            grid.CellMouseLeave += (s, e) => zoom.Hide(); grid.Scroll += (s, e) => zoom.Hide(); grid.MouseDown += (s, e) => zoom.Hide();
            selectedBox.Content.Controls.Add(grid);

            // ── CAD 미리보기
            var previewBox = new CadGroupPanel("CAD 미리보기") { Dock = DockStyle.Fill, Margin = Padding.Empty };
            preview = new LegendPreviewPanel(manifest, catalog) { Name = "LegendPreview", Dock = DockStyle.Fill, Margin = Padding.Empty };
            previewBox.AddTool(CadGlyph.ZoomOut, "ZoomOut", "축소", tips, (s, e) => preview.Zoom(1 / 1.25F)).AccessibleName = "미리보기 축소";
            previewBox.AddTool(CadGlyph.ZoomIn, "ZoomIn", "확대", tips, (s, e) => preview.Zoom(1.25F)).AccessibleName = "미리보기 확대";
            previewBox.AddTool(CadGlyph.Fit, "FitPreview", "창에 맞춤", tips, (s, e) => preview.Fit()).AccessibleName = "미리보기 창에 맞춤";
            previewBox.Content.Controls.Add(preview);

            columns.Controls.Add(source, 0, 0); columns.Controls.Add(selectedBox, 1, 0); columns.Controls.Add(previewBox, 2, 0);
            Controls.Add(columns);

            CadTheme.Apply(this); BackColor = CadTheme.Panel;
            CadTheme.Align(grid, "Facility", DataGridViewContentAlignment.MiddleLeft); CadTheme.Align(grid, "Spec", DataGridViewContentAlignment.MiddleLeft);
            CadTheme.Align(grid, "Quantity", DataGridViewContentAlignment.MiddleRight); CadTheme.Align(grid, "Symbol", DataGridViewContentAlignment.MiddleCenter);
            var quantity = grid.Columns["Quantity"].DefaultCellStyle; quantity.BackColor = CadTheme.Editable; quantity.ForeColor = CadTheme.Text; quantity.SelectionBackColor = CadTheme.Selected; quantity.SelectionForeColor = CadTheme.Text;
            grid.Columns["Symbol"].DefaultCellStyle.BackColor = CadTheme.Field; grid.Columns["Symbol"].DefaultCellStyle.Padding = new Padding(3);
            CadTheme.SetGridRowHeight(grid, ItemColumns.RowHeight); // 기호가 잘 보이도록 공용 표보다 조금 높게(배율·글꼴 반영)
            tips.BackColor = CadTheme.Panel; tips.ForeColor = CadTheme.Text;
            tips.SetToolTip(grid, "칸을 클릭해 명칭·규격·수량을 고칩니다. Enter로 확정, Delete로 행 빼기.");

            Model.Changed += OnModelChanged;
            RefreshSelection();
            VisibleChanged += (s, e) => { if (Visible) { ResizeCards(); preview.Fit(); } };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Model.Changed -= OnModelChanged; tips.Dispose(); noSymbol.Dispose(); zoom.Dispose();
                foreach (var image in symbols.Values) image.Dispose(); symbols.Clear();
            }
            base.Dispose(disposing);
        }

        private void QueueUi(Action action)
        {
            if (IsDisposed || Disposing) return;
            if (!IsHandleCreated) { action(); return; }
            try { BeginInvoke((Action)(() => { if (!IsDisposed && !Disposing) action(); })); }
            catch (InvalidOperationException) { if (!IsDisposed && !Disposing) throw; }
        }

        private void Say(string text, bool error = false) { var handler = Status; if (handler != null) handler(this, new StatusEventArgs(text, error)); }
        private void RaiseState() { var handler = StateChanged; if (handler != null) handler(this, EventArgs.Empty); }

        // 표를 다시 만드는 일은 지금 처리 중인 클릭·포커스 이동이 끝난 뒤에 합니다.
        // (표 이벤트 도중에 행을 지우면 "SetCurrentCellAddressCore 재진입" 오류가 납니다.)
        private void OnModelChanged(object sender, EventArgs e)
        {
            if (IsDisposed || Disposing) return;
            if (!IsHandleCreated) { RefreshSelection(); return; }
            if (refreshPending) return;
            refreshPending = true;
            QueueUi(() =>
            {
                refreshPending = false; RefreshSelection();
                if (pendingSelect != null) { string id = pendingSelect; pendingSelect = null; SelectRow(id); }
            });
        }

        private void Select(string id) { if (refreshPending) pendingSelect = id; else SelectRow(id); }

        // ---------------------------------------------------------------- 원본 항목

        private void ResizeCards()
        {
            if (cards.ClientSize.Width <= 0) return;
            int width = Math.Max(160, cards.ClientSize.Width - (cards.VerticalScroll.Visible ? 0 : SystemInformation.VerticalScrollBarWidth) - 1);
            cards.SuspendLayout();
            foreach (FacilityCard card in cards.Controls) if (card.Width != width || card.Height != (int)Math.Round(ItemColumns.RowHeight * DeviceDpi / 96.0)) card.Size = new Size(width, (int)Math.Round(ItemColumns.RowHeight * DeviceDpi / 96.0));
            cards.ResumeLayout(true);
        }

        private void FilterCards()
        {
            string term = search.Text.Trim();
            cards.SuspendLayout();
            foreach (FacilityCard card in cards.Controls)
                card.Visible = term.Length == 0 || (card.Row.Name + " " + card.Row.Spec + " " + card.Row.Remark + " " + card.Row.Unit).IndexOf(term, StringComparison.CurrentCultureIgnoreCase) >= 0;
            cards.ResumeLayout(); ResizeCards();
        }

        // ---------------------------------------------------------------- 선택 항목

        private Bitmap SymbolOf(LegendRow row)
        {
            Bitmap image;
            if (!symbols.TryGetValue(row.Id, out image)) { image = catalog.RenderSymbol(row, ItemColumns.Px(grid, 76), ItemColumns.Px(grid, ItemColumns.RowHeight - 8)); symbols.Add(row.Id, image); }
            return image;
        }

        /// <summary>선택 항목 표의 기호 칸에 마우스를 올리면 크게 보여 줍니다.</summary>
        private void HoverSymbol(int rowIndex, int columnIndex)
        {
            if (rowIndex < 0 || columnIndex != grid.Columns["Symbol"].Index || grid.IsCurrentCellInEditMode) { zoom.Hide(); return; }
            string id = grid.Rows[rowIndex].Tag as string;
            zoom.Hover(grid, grid.GetCellDisplayRectangle(columnIndex, rowIndex, false), id == null ? null : manifest.FindRow(id), null);
        }

        /// <summary>검사용 기호 크게 보기 창.</summary>
        internal SymbolZoom Zoom { get { return zoom; } }

        private void RefreshSelection()
        {
            refreshing = true;
            try
            {
                var selection = Model.Selection;
                if (!editing) RebuildGrid(selection);
                foreach (var pair in cardById) pair.Value.Picked = Model.Contains(pair.Key);
                selectedBox.Title = "선택 항목 (" + Model.Count + ")" + (Model.BlankCount > 0 ? " + 빈 행 " + Model.BlankCount : "");
                preview.SetSelection(selection);
            }
            finally { refreshing = false; }
            RaiseState();
        }

        private void RebuildGrid(IList<LegendSelection> selection)
        {
            string keep = CurrentId(); int column = grid.CurrentCell != null ? grid.CurrentCell.ColumnIndex : 1;
            grid.SuspendLayout(); grid.Rows.Clear();
            foreach (var item in selection)
            {
                int index;
                if (item.IsBlank)
                {
                    index = grid.Rows.Add(noSymbol, "(빈 행)", "", "");
                    var row = grid.Rows[index]; row.ReadOnly = true;
                    row.DefaultCellStyle.ForeColor = CadTheme.Muted; row.DefaultCellStyle.Font = CadTheme.Italic;
                    row.Cells["Facility"].ToolTipText = "공종 없이 빈 칸만 있는 행입니다. ↑↓로 위치를 옮길 수 있습니다.";
                }
                else
                {
                    var source = manifest.FindRow(item.ItemId);
                    index = grid.Rows.Add(SymbolOf(source), item.NameOverride ?? source.Name, item.SpecOverride ?? source.Spec, item.Quantity);
                    var row = grid.Rows[index];
                    row.Cells["Facility"].ToolTipText = "원본 명칭: " + source.Name + (item.NameOverride != null ? "  (수정됨)" : "");
                    row.Cells["Spec"].ToolTipText = "원본 규격: " + source.Spec + (string.IsNullOrEmpty(source.Unit) ? "" : " · 단위 " + source.Unit) + (item.SpecOverride != null ? "  (수정됨)" : "");
                    if (item.NameOverride != null) row.Cells["Facility"].Style.ForeColor = CadTheme.Success;
                    if (item.SpecOverride != null) row.Cells["Spec"].Style.ForeColor = CadTheme.Success;
                }
                grid.Rows[index].Tag = item.ItemId;
            }
            grid.ResumeLayout();
            SelectRow(keep, column);
        }

        public string CurrentId() { return grid.CurrentRow == null ? null : grid.CurrentRow.Tag as string; }

        private void SelectRow(string id, int column = 1)
        {
            if (id == null) return;
            var row = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => (string)r.Tag == id);
            if (row == null) return;
            int col = Math.Max(1, Math.Min(column, grid.Columns.Count - 1));
            if (!grid.Columns[col].Visible) col = 1;
            try { grid.CurrentCell = row.Cells[col]; } catch (InvalidOperationException) { }
        }

        // ---------------------------------------------------------------- 도구 막대에서 부르는 명령

        public bool CanRemove { get { return CurrentId() != null; } }
        public bool CanMove(int direction) { string id = CurrentId(); return id != null && Model.CanMove(id, direction); }
        public bool CanReset { get { string id = CurrentId(); return id != null && !LegendSelection.IsBlankId(id); } }
        public bool CanAddBlank { get { return manifest.SupportsBlankRows; } }
        public bool CanAddBlankAfter { get { return manifest.SupportsBlankRows && CurrentId() != null; } }

        public void ClearCategory() { if (CommitEdits()) { Model.Clear(); Say(manifest.CategoryName + " 선택을 비웠습니다"); } }
        public void AddBlankEnd() { if (CanAddBlank && CommitEdits()) Select(Model.AddBlank()); }
        public void AddBlankAfter() { string after = CurrentId(); if (CanAddBlank && after != null && CommitEdits()) Select(Model.AddBlank(after)); }
        public void ResetCurrent() { string id = CurrentId(); if (id != null && CommitEdits()) { Model.ResetLabels(id); Select(id); Say("원본 명칭·규격으로 되돌렸습니다"); } }
        public void MoveCurrent(int direction) { string id = CurrentId(); if (id != null && CommitEdits()) { Model.Move(id, direction); Select(id); } }

        public void RemoveCurrent()
        {
            string id = CurrentId(); if (id == null || !CommitEdits()) return;
            int index = grid.CurrentRow.Index;
            Model.Remove(id);
            var rest = Model.Selection;
            if (rest.Count > 0) Select(rest[Math.Min(index, rest.Count - 1)].ItemId);
        }

        private void ShowRowMenu(Point location)
        {
            if (!CommitEdits()) return;
            var menu = CadMenu.Create();
            string id = CurrentId();
            if (id != null)
            {
                CadMenu.Add(menu, "빼기 (Delete)", (s, e) => RemoveCurrent());
                CadMenu.Add(menu, "위로", (s, e) => MoveCurrent(-1), CanMove(-1));
                CadMenu.Add(menu, "아래로", (s, e) => MoveCurrent(1), CanMove(1));
                CadMenu.Add(menu, "원래 이름으로 (명칭·규격을 템플릿 원본으로)", (s, e) => ResetCurrent(), CanReset);
                menu.Items.Add(new ToolStripSeparator());
                CadMenu.Add(menu, "이 행 아래에 빈 행 넣기", (s, e) => AddBlankAfter(), CanAddBlankAfter);
            }
            CadMenu.Add(menu, "맨 아래에 빈 행 넣기", (s, e) => AddBlankEnd(), CanAddBlank);
            menu.Closed += (s, e) => QueueUi(menu.Dispose);
            menu.Show(grid, location);
        }

        public void ApplyPreset(LegendPreset preset)
        {
            if (preset == null || !CommitEdits()) return;
            try { Model.Apply(preset.Items); Say("‘" + preset.Name + "’ 적용"); }
            catch (ArgumentException ex) { Say("프리셋을 적용하지 못했습니다: " + ex.Message, true); }
        }

        public void ShowPresetMenu(Control anchor)
        {
            var menu = CadMenu.Create();
            CadMenu.AddHeader(menu, manifest.CategoryName + " 프리셋 불러오기");
            foreach (var preset in state.Presets) { var chosen = preset; CadMenu.Add(menu, (chosen.Id == state.Saved.DefaultPresetId ? "★ " : "    ") + chosen.Name, (s, e) => ApplyPreset(chosen)); }
            if (state.Saved.Presets.Count > 0)
            {
                menu.Items.Add(new ToolStripSeparator());
                foreach (var saved in state.Saved.Presets)
                {
                    var chosen = saved;
                    var sub = new ToolStripMenuItem("관리 · " + chosen.Name);
                    sub.DropDownItems.Add("이름 변경", null, (s, e) => ManagePreset(chosen, false));
                    sub.DropDownItems.Add("삭제", null, (s, e) => ManagePreset(chosen, true));
                    menu.Items.Add(sub);
                }
            }
            CadMenu.Add(menu, "시작 기본 구성 해제 (빈 목록으로 시작)", (s, e) => ChangePresets(() => state.Store.ClearDefault()));
            menu.Closed += (s, e) => QueueUi(menu.Dispose);
            CadMenu.Show(menu, anchor);
        }

        private void ChangePresets(Action action)
        {
            try { action(); state.ReloadSaved(); Say("프리셋 관리를 반영했습니다. 현재 선택 목록은 유지합니다."); }
            catch (Exception ex) when (ex is ArgumentException || ex is System.IO.IOException || ex is System.IO.InvalidDataException || ex is UnauthorizedAccessException || ex is System.Runtime.Serialization.SerializationException)
            { Say("프리셋을 변경하지 못했습니다: " + ex.Message, true); }
        }

        private void ManagePreset(LegendPreset preset, bool delete)
        {
            if (delete)
            {
                if (MessageBox.Show(this, "‘" + preset.Name + "’ 프리셋을 삭제할까요?", "프리셋 삭제", MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.OK)
                    ChangePresets(() => state.Store.Delete(preset.Id));
                return;
            }
            using (var dialog = new PresetNameForm(preset.Name, true))
                if (dialog.ShowDialog(this) == DialogResult.OK) ChangePresets(() => state.Store.Rename(preset.Id, dialog.PresetName));
        }

        public void SavePreset()
        {
            if (!CommitEdits()) return;
            if (Model.RowCount == 0) { Say("저장할 항목이 없습니다. 먼저 항목을 고르세요.", true); return; }
            using (var dialog = new PresetNameForm())
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var preset = state.Store.SaveDefault(dialog.PresetName, Model.Selection); state.ReloadSaved();
                    Say("‘" + preset.Name + "’ 저장 완료 · 다음 실행부터 이 구성으로 시작합니다");
                }
                catch (Exception ex) when (ex is ArgumentException || ex is System.IO.IOException || ex is UnauthorizedAccessException || ex is System.IO.InvalidDataException || ex is System.Runtime.Serialization.SerializationException)
                { Say("저장하지 못했습니다: " + ex.Message, true); }
            }
        }

        // ---------------------------------------------------------------- 칸 편집

        private static bool IsEditColumn(DataGridViewColumn column) { return column.Name == "Facility" || column.Name == "Spec" || column.Name == "Quantity"; }

        private void ValidateCell(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (refreshing || e.RowIndex < 0 || e.ColumnIndex < 0 || !IsEditColumn(grid.Columns[e.ColumnIndex]) || grid.Rows[e.RowIndex].ReadOnly) return;
            string message = Check(grid.Columns[e.ColumnIndex].Name, Convert.ToString(e.FormattedValue));
            grid.Rows[e.RowIndex].ErrorText = message ?? "";
            if (message != null) { e.Cancel = true; Say(message, true); }
        }

        private static string Check(string column, string text)
        {
            try { if (column == "Quantity") PickerModel.NormalizeQuantity(text); else LegendSelection.NormalizeLabel(text); return null; }
            catch (ArgumentException ex) { return ex.Message; }
        }

        private void CommitCell(object sender, DataGridViewCellEventArgs e)
        {
            if (refreshing || e.RowIndex < 0 || e.ColumnIndex < 0 || !IsEditColumn(grid.Columns[e.ColumnIndex])) return;
            var row = grid.Rows[e.RowIndex]; string id = row.Tag as string;
            if (id == null || LegendSelection.IsBlankId(id)) return;
            editing = true;
            try
            {
                if (grid.Columns[e.ColumnIndex].Name == "Quantity")
                {
                    string quantity = PickerModel.NormalizeQuantity(Convert.ToString(row.Cells["Quantity"].Value));
                    row.Cells["Quantity"].Value = quantity; Model.SetQuantity(id, quantity);
                }
                else
                {
                    Model.SetLabels(id, Convert.ToString(row.Cells["Facility"].Value), Convert.ToString(row.Cells["Spec"].Value));
                    var item = Model.Selection.Single(x => x.ItemId == id); var source = manifest.FindRow(id);
                    row.Cells["Facility"].Value = item.NameOverride ?? source.Name; row.Cells["Spec"].Value = item.SpecOverride ?? source.Spec;
                    row.Cells["Facility"].Style.ForeColor = item.NameOverride != null ? CadTheme.Success : Color.Empty;
                    row.Cells["Spec"].Style.ForeColor = item.SpecOverride != null ? CadTheme.Success : Color.Empty;
                }
                row.ErrorText = "";
                Say("수정 반영됨 · 초록 글자는 원본과 다르게 고친 칸입니다");
            }
            catch (ArgumentException ex) { row.ErrorText = ex.Message; Say(ex.Message, true); }
            finally { editing = false; }
        }

        /// <summary>입력 중인 칸을 확정합니다. 형식이 틀리면 false를 돌려주고 입력을 유지합니다.</summary>
        public bool CommitEdits()
        {
            if (grid.IsCurrentCellInEditMode && grid.CurrentCell != null && IsEditColumn(grid.CurrentCell.OwningColumn))
            {
                string message = Check(grid.CurrentCell.OwningColumn.Name, Convert.ToString(grid.CurrentCell.EditedFormattedValue));
                if (message != null) { grid.CurrentCell.OwningRow.ErrorText = message; Say(message, true); return false; }
                grid.CurrentCell.OwningRow.ErrorText = "";
            }
            return grid.EndEdit();
        }

        public void CancelEdits() { AutoValidate = AutoValidate.Disable; grid.CancelEdit(); }

        private sealed class PresetNameForm : Form
        {
            private readonly TextBox name;
            public string PresetName { get { return name.Text.Trim(); } }
            public PresetNameForm(string initial = "내 기본 구성", bool rename = false)
            {
                Text = rename ? "프리셋 이름 변경" : "프리셋 저장"; Font = CadTheme.Ui; AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(380, 150);
                FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
                Controls.Add(new Label { Text = "저장할 이름 (같은 이름이면 덮어씁니다)", Location = new Point(16, 14), Size = new Size(350, 22) });
                name = new TextBox { Text = initial, MaxLength = 40, Location = new Point(16, 42), Size = new Size(348, 26) }; Controls.Add(name);
                var ok = new CadIconButton(CadGlyph.Save, "저장") { Location = new Point(264, 96), Size = new Size(100, 34), Primary = true };
                ok.Click += (s, e) => { if (PresetName.Length > 0) { DialogResult = DialogResult.OK; Close(); } };
                var cancel = new CadIconButton(CadGlyph.None, "취소") { DialogResult = DialogResult.Cancel, Location = new Point(156, 96), Size = new Size(100, 34) };
                Controls.Add(ok); Controls.Add(cancel); AcceptButton = ok; CancelButton = cancel;
                CadTheme.Apply(this);
                Shown += (s, e) => { name.Focus(); name.SelectAll(); };
            }
        }
    }
}
