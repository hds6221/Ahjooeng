// CadUi — Autocad_auto 공용 화면 템플릿(AutoCAD 2020 어두운 회색 스타일).
// 새 명령은 이 폴더의 *.cs를 프로젝트에 링크해서 씁니다(README.md 참고). DLL로 나누지 않아 명령끼리 버전 충돌이 없습니다.
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CadUi
{
    /// <summary>색·글꼴·크기 기준. 모든 명령 화면이 같은 값을 씁니다.</summary>
    public static class CadTheme
    {
        // ── 색
        public static readonly Color Window = Color.FromArgb(60, 64, 70);        // 창 바탕
        public static readonly Color Panel = Color.FromArgb(50, 54, 60);         // 묶음 상자 바탕
        public static readonly Color PanelBorder = Color.FromArgb(82, 88, 96);
        public static readonly Color Header = Color.FromArgb(70, 75, 82);        // 표 머리글·탭
        public static readonly Color Field = Color.FromArgb(43, 47, 52);         // 입력칸·목록 행
        public static readonly Color Alternate = Color.FromArgb(47, 51, 57);
        public static readonly Color GridLine = Color.FromArgb(66, 71, 78);
        public static readonly Color Button = Color.FromArgb(72, 77, 84);
        public static readonly Color ButtonHover = Color.FromArgb(86, 92, 100);
        public static readonly Color ButtonBorder = Color.FromArgb(98, 104, 112);
        public static readonly Color Text = Color.FromArgb(228, 231, 235);
        public static readonly Color Muted = Color.FromArgb(168, 175, 184);
        public static readonly Color Disabled = Color.FromArgb(118, 124, 132);
        public static readonly Color Selected = Color.FromArgb(56, 96, 138);     // 선택 행(차분한 강철 파랑)
        public static readonly Color SelectedBorder = Color.FromArgb(96, 146, 196);
        public static readonly Color Primary = Color.FromArgb(58, 118, 178);     // 주 버튼·선택 탭
        public static readonly Color PrimaryHover = Color.FromArgb(72, 134, 196);
        public static readonly Color Editable = Color.FromArgb(52, 54, 50);      // 직접 고치는 칸
        public static readonly Color Paper = Color.White;                         // CAD 미리보기 종이
        public static readonly Color Ink = Color.FromArgb(40, 48, 58);
        public static readonly Color Error = Color.FromArgb(255, 170, 160);
        public static readonly Color Success = Color.FromArgb(150, 214, 160);
        public static readonly Color IconBlue = Color.FromArgb(82, 160, 232);
        public static readonly Color IconRed = Color.FromArgb(232, 82, 72);
        public static readonly Color IconYellow = Color.FromArgb(236, 186, 64);

        // ── 글꼴(한 번만 만들어 공유)
        public const string FontName = "맑은 고딕";
        public static readonly Font Ui = new Font(FontName, 9F);
        public static readonly Font UiBold = new Font(FontName, 9F, FontStyle.Bold);
        public static readonly Font Title = new Font(FontName, 9.5F, FontStyle.Bold);
        public static readonly Font Small = new Font(FontName, 8.25F);
        public static readonly Font Italic = new Font(FontName, 8.75F, FontStyle.Italic);

        // ── 크기
        public const int RowHeight = 26;
        public const int ToolButton = 30;

        /// <summary>96 DPI 기준 픽셀을 이 컨트롤의 화면 배율(125~200%)에 맞춥니다.</summary>
        public static int Px(Control control, int value) { return (int)Math.Round(value * (control != null && control.DeviceDpi > 0 ? control.DeviceDpi : 96) / 96.0); }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Font, Font> bold = new System.Runtime.CompilerServices.ConditionalWeakTable<Font, Font>();
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Font, Font> small = new System.Runtime.CompilerServices.ConditionalWeakTable<Font, Font>();
        /// <summary>직접 그리는 굵은 글씨: 고정 글꼴(UiBold) 대신 컨트롤 글꼴을 따릅니다(배율·글꼴 크기 반영).</summary>
        public static Font BoldOf(Control control) { var f = control.Font; return f.Bold ? f : bold.GetValue(f, x => new Font(x, FontStyle.Bold)); }
        /// <summary>직접 그리는 작은 글씨(Small과 Ui의 비율 유지).</summary>
        public static Font SmallOf(Control control) { return small.GetValue(control.Font, x => new Font(x.FontFamily, x.SizeInPoints * Small.SizeInPoints / Ui.SizeInPoints, x.Style)); }

        /// <summary>컨트롤 묶음 전체에 기본 색을 입힙니다. 안쪽 패널·글자는 부모 바탕을 물려받습니다.</summary>
        public static void Apply(Control root)
        {
            if (root == null) throw new ArgumentNullException("root");
            if (root.Tag is KeepStyle) { foreach (Control child in root.Controls) Apply(child); return; }
            root.ForeColor = Text;
            if (root is Form || root is UserControl) root.BackColor = Window;
            var grid = root as DataGridView;
            if (grid != null) { StyleGrid(grid); return; }
            var button = root as Button;
            if (button != null && !(button is CadIconButton)) StyleButton(button, false);
            if (root is TextBoxBase || root is ComboBox || root is ListBox) root.BackColor = Field;
            var text = root as TextBoxBase; if (text != null) text.BorderStyle = BorderStyle.FixedSingle;
            var combo = root as ComboBox;
            if (combo != null)
            {
                combo.FlatStyle = FlatStyle.Flat;
                if (combo.DropDownStyle == ComboBoxStyle.DropDownList)
                {
                    combo.DrawMode = DrawMode.OwnerDrawFixed; combo.ItemHeight = Math.Max(18, combo.Font.Height + 4);
                    combo.DrawItem -= DrawComboItem; combo.DrawItem += DrawComboItem;
                }
            }
            foreach (Control child in root.Controls) Apply(child);
        }

        /// <summary>Apply가 이 컨트롤의 색을 바꾸지 않게 표시합니다(자식은 계속 적용).</summary>
        public sealed class KeepStyle { }
        public static void Keep(Control control) { control.Tag = new KeepStyle(); }

        public static void StyleButton(Button button, bool primary)
        {
            button.FlatStyle = FlatStyle.Flat; button.UseVisualStyleBackColor = false;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = primary ? Primary : ButtonBorder;
            button.BackColor = primary ? Primary : Button; button.ForeColor = Text;
            button.FlatAppearance.MouseOverBackColor = primary ? PrimaryHover : ButtonHover;
            button.FlatAppearance.MouseDownBackColor = primary ? Primary : Selected;
        }

        public static void StyleGrid(DataGridView grid)
        {
            grid.EnableHeadersVisualStyles = false; grid.BorderStyle = BorderStyle.None;
            grid.BackgroundColor = Field; grid.GridColor = GridLine;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.Single;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            grid.RowHeadersVisible = false;
            Set(grid.ColumnHeadersDefaultCellStyle, Header, Text, Header, Text); grid.ColumnHeadersDefaultCellStyle.Font = Ui;
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 4, 0); grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            Set(grid.DefaultCellStyle, Field, Text, Selected, Text); grid.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
            Set(grid.RowsDefaultCellStyle, Field, Text, Selected, Text);
            Set(grid.AlternatingRowsDefaultCellStyle, Alternate, Text, Selected, Text);
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            SetGridRowHeight(grid, RowHeight);
            grid.FontChanged -= RefitGrid; grid.FontChanged += RefitGrid;
            grid.DpiChangedAfterParent -= RefitGrid; grid.DpiChangedAfterParent += RefitGrid;
            grid.EditingControlShowing -= StyleEditor; grid.EditingControlShowing += StyleEditor;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DataGridView, object> gridRowBase = new System.Runtime.CompilerServices.ConditionalWeakTable<DataGridView, object>();

        /// <summary>
        /// 표의 줄·머리글 높이를 정합니다(96 DPI 기준 baseHeight). 화면 배율(DeviceDpi)과 글꼴 크기 중 큰 쪽을 따라
        /// 125~200%나 큰 글꼴에서도 글자가 잘리지 않습니다. 글꼴·배율이 바뀌면 다시 맞춥니다.
        /// </summary>
        public static void SetGridRowHeight(DataGridView grid, int baseHeight)
        {
            gridRowBase.Remove(grid); gridRowBase.Add(grid, baseHeight);
            FitGrid(grid, baseHeight);
        }

        private static void RefitGrid(object sender, EventArgs e)
        {
            var grid = (DataGridView)sender; object stored;
            FitGrid(grid, gridRowBase.TryGetValue(grid, out stored) ? (int)stored : RowHeight);
        }

        private static void FitGrid(DataGridView grid, int baseHeight)
        {
            double scale = (grid.DeviceDpi > 0 ? grid.DeviceDpi : 96) / 96.0;
            // 글꼴이 바뀌는 순간에는 셀 스타일 글꼴이 아직 예전 값일 수 있어 표 글꼴과 스타일 글꼴 중 큰 쪽을 씁니다.
            Func<Font, int> text = font => font == null ? 0 : TextRenderer.MeasureText("가Ag", font).Height;
            int cellText = Math.Max(text(grid.Font), text(grid.DefaultCellStyle.Font)), headerText = Math.Max(text(grid.Font), text(grid.ColumnHeadersDefaultCellStyle.Font));
            int row = Math.Max((int)Math.Round(baseHeight * scale), cellText + (int)Math.Round(10 * scale));
            int header = Math.Max((int)Math.Round(26 * scale), headerText + (int)Math.Round(8 * scale));
            grid.ColumnHeadersHeight = header;
            grid.RowTemplate.Height = row; foreach (DataGridViewRow r in grid.Rows) r.Height = row;
        }

        private static void Set(DataGridViewCellStyle style, Color back, Color fore, Color selectionBack, Color selectionFore)
        { style.BackColor = back; style.ForeColor = fore; style.SelectionBackColor = selectionBack; style.SelectionForeColor = selectionFore; }

        public static void Align(DataGridView grid, string column, DataGridViewContentAlignment alignment)
        {
            if (!grid.Columns.Contains(column)) return;
            grid.Columns[column].DefaultCellStyle.Alignment = alignment; grid.Columns[column].HeaderCell.Style.Alignment = alignment;
        }

        private static void StyleEditor(object sender, DataGridViewEditingControlShowingEventArgs e)
        {
            var grid = (DataGridView)sender;
            e.Control.BackColor = grid.CurrentCell == null ? Field : grid.CurrentCell.InheritedStyle.BackColor == Selected ? Field : grid.CurrentCell.InheritedStyle.BackColor;
            e.Control.ForeColor = Text;
        }

        private static void DrawComboItem(object sender, DrawItemEventArgs e)
        {
            var combo = (ComboBox)sender; bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var brush = new SolidBrush(selected ? Selected : Field)) e.Graphics.FillRectangle(brush, e.Bounds);
            if (e.Index >= 0 && e.Index < combo.Items.Count)
                TextRenderer.DrawText(e.Graphics, combo.GetItemText(combo.Items[e.Index]), combo.Font, new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height), combo.Enabled ? Text : Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        /// <summary>비어 있는 입력칸에 흐린 안내 글자를 보여줍니다.</summary>
        public static void SetPlaceholder(TextBox box, string text)
        {
            if (box.IsHandleCreated) SendMessage(box.Handle, 0x1501, new IntPtr(1), text);
            else box.HandleCreated += (s, e) => SendMessage(box.Handle, 0x1501, new IntPtr(1), text);
        }

        /// <summary>한 줄짜리 표 배치에 100% 행을 지정합니다(없으면 내용 크기로 늘어나 칸 밖으로 넘칩니다).</summary>
        public static TableLayoutPanel SingleRow(TableLayoutPanel panel)
        {
            panel.RowCount = 1; panel.RowStyles.Clear(); panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); return panel;
        }
    }
}
