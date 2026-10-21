using System;
using System.Drawing;
using System.Windows.Forms;

namespace CadUi
{
    /// <summary>
    /// 모든 명령 창의 공통 틀(UI 디자인 시안 03 AUTOCAD 스타일).
    /// ┌ 탭 줄(선택) ─────────────────────────────┐
    /// │ 도구 막대: 이름표 · 입력 · 아이콘 버튼        │
    /// │ 본문(Body): 묶음 상자 여러 개                 │
    /// │ [⚙ 보조 버튼]   안내 문구          [주 버튼] │
    /// └───────────────────────────────────────┘
    /// 상속한 뒤 Tabs/Toolbar/Body/Footer에 컨트롤을 넣고 마지막에 FinishLayout()을 부릅니다.
    /// </summary>
    public class CadToolWindow : Form
    {
        public const int DefaultWidth = 1120, DefaultHeight = 585;

        protected readonly ToolTip Tips = new ToolTip { AutoPopDelay = 15000, InitialDelay = 350 };
        public CadTabStrip Tabs { get; private set; }
        public FlowLayoutPanel Toolbar { get; private set; }
        public Panel Body { get; private set; }
        public FlowLayoutPanel FooterLeft { get; private set; }
        public FlowLayoutPanel FooterRight { get; private set; }
        public Label Summary { get; private set; }
        public Label Status { get; private set; }
        /// <summary>Esc로 닫는 숨은 취소 버튼(Name = "CancelWindow").</summary>
        public Button CancelAction { get; private set; }

        public CadToolWindow(string title, bool withTabs)
        {
            Text = title; Font = CadTheme.Ui; AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.Manual;
            ShowIcon = false; ShowInTaskbar = false; KeyPreview = true; BackColor = CadTheme.Window; ForeColor = CadTheme.Text;
            var area = HostScreen().WorkingArea;
            MinimumSize = new Size(900, 480); ClientSize = new Size(Math.Min(DefaultWidth, area.Width - 40), Math.Min(DefaultHeight, area.Height - 60));

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty, Padding = new Padding(8, 6, 8, 6) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, withTabs ? 30 : 0)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            Tabs = new CadTabStrip { Name = "TabStrip", Dock = DockStyle.Fill, Margin = Padding.Empty, Visible = withTabs };
            var frame = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = CadTheme.Panel };
            Toolbar = new FlowLayoutPanel { Name = "Toolbar", Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(6, 5, 6, 0), BackColor = CadTheme.Panel };
            Body = new Panel { Name = "Body", Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(6, 0, 6, 6), BackColor = CadTheme.Panel };
            CadTheme.Keep(Toolbar); CadTheme.Keep(Body); CadTheme.Keep(frame);
            var footer = CadTheme.SingleRow(new TableLayoutPanel { Name = "Footer", Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty, Padding = new Padding(0, 6, 0, 0) });
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            FooterLeft = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Anchor = AnchorStyles.Left };
            FooterRight = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Anchor = AnchorStyles.Right, FlowDirection = FlowDirection.RightToLeft };
            var texts = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Margin = new Padding(10, 0, 10, 0) };
            texts.RowStyles.Add(new RowStyle(SizeType.Percent, 52)); texts.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
            Summary = new Label { Name = "Summary", Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft, Font = CadTheme.UiBold, AutoEllipsis = true, Margin = Padding.Empty };
            Status = new Label { Name = "StatusMessage", Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopLeft, Font = CadTheme.Small, AutoEllipsis = true, Margin = Padding.Empty };
            texts.Controls.Add(Summary, 0, 0); texts.Controls.Add(Status, 0, 1);
            footer.Controls.Add(FooterLeft, 0, 0); footer.Controls.Add(texts, 1, 0); footer.Controls.Add(FooterRight, 2, 0);
            frame.Controls.Add(Body); frame.Controls.Add(Toolbar); Toolbar.Dock = DockStyle.Top; Toolbar.Height = 40;
            root.Controls.Add(Tabs, 0, 0); root.Controls.Add(frame, 0, 1); root.SetRowSpan(frame, 2); root.Controls.Add(footer, 0, 3);
            CancelAction = new Button { Name = "CancelWindow", Size = new Size(1, 1), Location = new Point(-10, -10), TabStop = false, CausesValidation = false };
            CancelAction.Click += (s, e) => { OnCancel(); DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(root); Controls.Add(CancelAction); CancelButton = CancelAction;
        }

        /// <summary>AutoCAD 본 창이 있는 화면(없으면 마우스가 있는 화면).</summary>
        private static Screen HostScreen()
        {
            try
            {
                var main = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (main != IntPtr.Zero) return Screen.FromHandle(main);
            }
            catch (InvalidOperationException) { }
            return Screen.FromPoint(Cursor.Position);
        }

        /// <summary>창을 띄울 때마다 AutoCAD가 있는 화면의 정가운데에 둡니다(작업 표시줄 제외 영역 기준).</summary>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            var area = HostScreen().WorkingArea;
            if (Width > area.Width) Width = area.Width;
            if (Height > area.Height) Height = area.Height;
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
        }

        /// <summary>Esc·취소로 닫기 직전에 불립니다(입력 중 내용 버리기 등).</summary>
        protected virtual void OnCancel() { }

        /// <summary>생성자 끝에서 한 번 부릅니다. 색을 입히고 안내 글자 색을 맞춥니다.</summary>
        protected void FinishLayout()
        {
            CadTheme.Apply(this);
            Summary.ForeColor = CadTheme.Text; Status.ForeColor = CadTheme.Muted;
            Tips.BackColor = CadTheme.Panel; Tips.ForeColor = CadTheme.Text;
        }

        public void SetStatus(string text, bool error = false) { Status.Text = text ?? ""; Status.ForeColor = error ? CadTheme.Error : CadTheme.Muted; }

        // ── 도구 막대 도우미
        public Label AddToolbarLabel(string text, int width = 0)
        {
            var label = new Label { Text = text, AutoSize = width == 0, Width = width, Height = CadTheme.ToolButton, TextAlign = ContentAlignment.MiddleLeft, Font = CadTheme.UiBold, Margin = new Padding(2, 0, 8, 0) };
            Toolbar.Controls.Add(label); return label;
        }

        public CadIconButton AddToolbarButton(CadGlyph glyph, string name, string tip, EventHandler click, string text = "")
        {
            var button = new CadIconButton(glyph, text) { Name = name };
            button.Click += click; Tips.SetToolTip(button, tip); Toolbar.Controls.Add(button); return button;
        }

        public void AddToolbarGap(int width = 14) { Toolbar.Controls.Add(new Panel { Width = width, Height = CadTheme.ToolButton, Margin = Padding.Empty }); }

        public CadIconButton AddFooterButton(CadGlyph glyph, string name, string text, string tip, EventHandler click, bool primary)
        {
            var button = new CadIconButton(glyph, text) { Name = name, Primary = primary, Height = 32, Margin = primary ? new Padding(8, 0, 0, 0) : new Padding(0, 0, 8, 0) };
            if (primary) button.Width = Math.Max(150, button.Width + 20);
            button.Click += click; Tips.SetToolTip(button, tip);
            (primary ? FooterRight : FooterLeft).Controls.Add(button); return button;
        }

        protected override void Dispose(bool disposing) { if (disposing) Tips.Dispose(); base.Dispose(disposing); }
    }
}
