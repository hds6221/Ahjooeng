using System;
using System.Drawing;
using System.Windows.Forms;

namespace CadUi
{
    /// <summary>
    /// 설정·확인용 작은 창(공용). 항목마다 굵은 제목 + 쉬운 설명 + 선택 컨트롤을 세로로 쌓습니다.
    /// 아래 줄: [기본값으로]  ……  [취소] [저장].
    /// </summary>
    /// <summary>숫자만 받는 입력칸(정수). 범위 밖이면 빨간 테두리.</summary>
    public class CadNumberField : CadTextField
    {
        public decimal Minimum { get; private set; }
        public decimal Maximum { get; private set; }
        public CadNumberField(decimal min, decimal max) : base(null, CadGlyph.None)
        {
            Minimum = min; Maximum = max; Box.TextAlign = HorizontalAlignment.Right;
            Box.KeyPress += (s, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };
            TextChanged += (s, e) => Invalidate();
        }
        public bool IsValid { get { decimal v; return decimal.TryParse(Text.Trim(), out v) && v >= Minimum && v <= Maximum; } }
        public decimal Value
        {
            get { decimal v; return decimal.TryParse(Text.Trim(), out v) ? Math.Max(Minimum, Math.Min(Maximum, v)) : Minimum; }
            set { Text = Math.Max(Minimum, Math.Min(Maximum, value)).ToString("0"); }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!IsValid) using (var pen = new Pen(CadTheme.Error, 1.5F)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    public class CadDialog : Form
    {
        protected readonly ToolTip Tips = new ToolTip { AutoPopDelay = 15000, InitialDelay = 300 };
        private readonly FlowLayoutPanel body;
        /// <summary>지금 섹션의 선택 단추 묶음. 섹션마다 따로 두어야 다른 섹션의 단추가 서로 꺼지지 않습니다.</summary>
        private FlowLayoutPanel choiceGroup;
        private readonly FlowLayoutPanel footerLeft, footerRight;
        private const int ContentWidth = 470;

        public CadDialog(string title, string intro)
        {
            Text = title; Font = CadTheme.Ui; AutoScaleMode = AutoScaleMode.Dpi; FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowIcon = false; ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent; KeyPreview = true;
            body = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(16, 12, 16, 4) };
            var footer = CadTheme.SingleRow(new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 52, ColumnCount = 2, Padding = new Padding(14, 8, 14, 10), BackColor = CadTheme.Panel });
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footerLeft = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
            footerRight = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = Padding.Empty, FlowDirection = FlowDirection.RightToLeft };
            footer.Controls.Add(footerLeft, 0, 0); footer.Controls.Add(footerRight, 1, 0);
            CadTheme.Keep(footer);
            Controls.Add(body); Controls.Add(footer);
            if (!string.IsNullOrEmpty(intro)) body.Controls.Add(new Label { Text = intro, AutoSize = true, MaximumSize = new Size(ContentWidth, 0), Margin = new Padding(0, 0, 0, 10), ForeColor = CadTheme.Muted, Tag = "muted" });
        }

        /// <summary>굵은 제목과 한두 줄 설명. 설명은 처음 쓰는 사람이 이해할 수 있는 말로 씁니다.</summary>
        public void AddSection(string title, string description = null)
        {
            choiceGroup = null;
            if (body.Controls.Count > 1) body.Controls.Add(new Panel { Width = ContentWidth, Height = 1, BackColor = CadTheme.PanelBorder, Margin = new Padding(0, 8, 0, 10), Tag = new CadTheme.KeepStyle() });
            body.Controls.Add(new Label { Text = title, AutoSize = true, Font = CadTheme.Title, Margin = new Padding(0, 0, 0, 2) });
            if (!string.IsNullOrEmpty(description))
                body.Controls.Add(new Label { Text = description, AutoSize = true, MaximumSize = new Size(ContentWidth, 0), Margin = new Padding(0, 0, 0, 6), Tag = "muted" });
        }

        /// <summary>둥근 선택 단추 + 아래 작은 설명.</summary>
        public RadioButton AddChoice(string name, string text, string hint, bool isChecked)
        {
            if (choiceGroup == null)
            {
                choiceGroup = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty, Padding = Padding.Empty };
                body.Controls.Add(choiceGroup);
            }
            RadioButton radio = new CadRadioButton { Name = name, Text = text, Checked = isChecked, Margin = new Padding(4, 2, 0, 0) };
            choiceGroup.Controls.Add(radio); AddHint(hint, 24, choiceGroup); return radio;
        }

        public CheckBox AddCheck(string name, string text, string hint, bool isChecked)
        {
            CheckBox check = new CadCheckBox { Name = name, Text = text, Checked = isChecked, Margin = new Padding(4, 2, 0, 0) };
            body.Controls.Add(check); AddHint(hint, 24); return check;
        }

        /// <summary>숫자 칸 + 단위 + 설명. 범위를 벗어나거나 숫자가 아니면 칸이 빨간 테두리로 바뀌고 저장되지 않습니다.</summary>
        public CadNumberField AddNumber(string name, string label, decimal value, decimal min, decimal max, string unit, string hint)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(4, 2, 0, 0) };
            row.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 6, 8, 0), Font = CadTheme.UiBold });
            var number = new CadNumberField(min, max) { Name = name, Width = 90, Margin = new Padding(0, 2, 0, 0) };
            number.Value = value;
            row.Controls.Add(number);
            row.Controls.Add(new Label { Text = unit, AutoSize = true, Margin = new Padding(6, 6, 0, 0), Tag = "muted" });
            body.Controls.Add(row); AddHint(hint, 4); return number;
        }

        /// <summary>이름: 값 한 줄(값은 마우스로 골라 복사할 수 있음).</summary>
        public void AddInfo(string label, string value)
        {
            var row = new TableLayoutPanel { ColumnCount = 2, Width = ContentWidth, AutoSize = true, Margin = new Padding(4, 2, 0, 4) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.Controls.Add(new Label { Text = label, AutoSize = true, Font = CadTheme.UiBold, Margin = new Padding(0, 3, 6, 0) }, 0, 0);
            var text = new TextBox { Text = value ?? "", ReadOnly = true, BorderStyle = BorderStyle.None, Multiline = true, WordWrap = true, Width = ContentWidth - 120, BackColor = CadTheme.Window, ForeColor = CadTheme.Text, Margin = new Padding(0, 3, 0, 0), TabStop = false };
            CadTheme.Keep(text);
            using (var g = text.CreateGraphics()) text.Height = Math.Max(18, TextRenderer.MeasureText(g, text.Text, CadTheme.Ui, new Size(text.Width, 0), TextFormatFlags.WordBreak).Height + 4);
            row.Controls.Add(text, 1, 0);
            body.Controls.Add(row);
        }

        private void AddHint(string hint, int indent, Control into = null)
        {
            if (string.IsNullOrEmpty(hint)) return;
            (into ?? body).Controls.Add(new Label { Text = hint, AutoSize = true, MaximumSize = new Size(ContentWidth - indent, 0), Margin = new Padding(indent, 0, 0, 6), Font = CadTheme.Small, Tag = "muted" });
        }

        public CadIconButton AddButton(string name, string text, bool primary, EventHandler click, bool left = false, CadGlyph glyph = CadGlyph.None)
        {
            var button = new CadIconButton(glyph, text) { Name = name, Primary = primary, Height = 32, Margin = left ? new Padding(0, 0, 8, 0) : new Padding(8, 0, 0, 0) };
            button.Width = Math.Max(88, button.Width); button.Click += click;
            (left ? footerLeft : footerRight).Controls.Add(button); return button;
        }

        /// <summary>생성자 끝에서 한 번: 색을 입히고 내용 높이에 맞춰 창 크기를 정합니다.</summary>
        protected void FinishLayout()
        {
            CadTheme.Apply(this);
            foreach (Control c in body.Controls) Mute(c);
            Tips.BackColor = CadTheme.Panel; Tips.ForeColor = CadTheme.Text;
            body.PerformLayout();
            int height = 0; foreach (Control c in body.Controls) height = Math.Max(height, c.Bottom + c.Margin.Bottom);
            ClientSize = new Size(ContentWidth + 40, Math.Min(Screen.FromPoint(Cursor.Position).WorkingArea.Height - 80, height + body.Padding.Vertical + 60));
        }

        private static void Mute(Control control)
        {
            if ("muted".Equals(control.Tag)) control.ForeColor = CadTheme.Muted;
            foreach (Control child in control.Controls) Mute(child);
        }

        protected override void Dispose(bool disposing) { if (disposing) Tips.Dispose(); base.Dispose(disposing); }
    }
}

namespace CadUi
{
    /// <summary>어두운 바탕에서도 고른 것이 분명한 선택 단추(파란 테두리 + 가운데 점).</summary>
    public class CadRadioButton : RadioButton
    {
        public CadRadioButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = CadTheme.UiBold; Cursor = Cursors.Hand; AutoSize = false; Height = 24;
        }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Width = TextRenderer.MeasureText(Text, Font).Width + 30; }
        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Parent != null ? Parent.BackColor : CadTheme.Window); g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var circle = new Rectangle(2, (Height - 16) / 2, 16, 16);
            using (var fill = new SolidBrush(CadTheme.Field)) g.FillEllipse(fill, circle);
            using (var pen = new Pen(Checked ? CadTheme.IconBlue : CadTheme.ButtonBorder, Checked ? 2F : 1.4F)) g.DrawEllipse(pen, circle);
            if (Checked) using (var dot = new SolidBrush(CadTheme.IconBlue)) g.FillEllipse(dot, Rectangle.Inflate(circle, -4, -4));
            TextRenderer.DrawText(g, Text, Font, new Rectangle(24, 0, Width - 24, Height), Enabled ? CadTheme.Text : CadTheme.Disabled, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, new Rectangle(22, 2, Width - 23, Height - 4));
        }
    }

    /// <summary>어두운 바탕에서도 켜짐이 분명한 체크 상자(파란 칸 + ✓).</summary>
    public class CadCheckBox : CheckBox
    {
        public CadCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = CadTheme.UiBold; Cursor = Cursors.Hand; AutoSize = false; Height = 24;
        }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Width = TextRenderer.MeasureText(Text, Font).Width + 30; }
        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Parent != null ? Parent.BackColor : CadTheme.Window); g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var box = new Rectangle(2, (Height - 16) / 2, 16, 16);
            using (var fill = new SolidBrush(Checked ? CadTheme.Primary : CadTheme.Field)) g.FillRectangle(fill, box);
            using (var pen = new Pen(Checked ? CadTheme.IconBlue : CadTheme.ButtonBorder, 1.4F)) g.DrawRectangle(pen, box);
            if (Checked) using (var tick = new Pen(Color.White, 2F)) g.DrawLines(tick, new[] { new Point(box.Left + 3, box.Top + 8), new Point(box.Left + 7, box.Top + 12), new Point(box.Right - 3, box.Top + 4) });
            TextRenderer.DrawText(g, Text, Font, new Rectangle(24, 0, Width - 24, Height), Enabled ? CadTheme.Text : CadTheme.Disabled, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, new Rectangle(22, 2, Width - 23, Height - 4));
        }
    }
}
