using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CadUi
{
    /// <summary>도구 막대 아이콘(이미지 파일 없이 직접 그림).</summary>
    public enum CadGlyph { None, New, Open, Save, Delete, Up, Down, Settings, Generate, Search, InsertEnd, InsertBelow, Reset, ZoomIn, ZoomOut, Fit, Template, Help, Trash, Dropdown }

    public static class GlyphPainter
    {
        public static void Draw(Graphics g, CadGlyph glyph, Rectangle r, bool enabled)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Func<Color, Color> c = color => enabled ? color : CadTheme.Disabled;
            float x = r.X, y = r.Y, w = r.Width, h = r.Height;
            using (var white = new SolidBrush(c(Color.FromArgb(240, 242, 245))))
            using (var outline = new Pen(c(Color.FromArgb(150, 156, 164)), 1.2F))
            using (var blue = new Pen(c(CadTheme.IconBlue), 2.6F) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var blueFill = new SolidBrush(c(CadTheme.IconBlue)))
            using (var red = new Pen(c(CadTheme.IconRed), 2.6F) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var light = new Pen(c(CadTheme.Text), 1.8F) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                switch (glyph)
                {
                    case CadGlyph.New:
                    {
                        var p = new[] { new PointF(x + w * .22F, y + h * .08F), new PointF(x + w * .62F, y + h * .08F), new PointF(x + w * .8F, y + h * .28F), new PointF(x + w * .8F, y + h * .92F), new PointF(x + w * .22F, y + h * .92F) };
                        g.FillPolygon(white, p); g.DrawPolygon(outline, p); break;
                    }
                    case CadGlyph.Open:
                        using (var yellow = new SolidBrush(c(CadTheme.IconYellow)))
                        using (var dark = new SolidBrush(c(Color.FromArgb(200, 150, 40))))
                        {
                            g.FillRectangle(dark, x + w * .08F, y + h * .2F, w * .36F, h * .16F);
                            g.FillRectangle(dark, x + w * .08F, y + h * .3F, w * .84F, h * .55F);
                            g.FillPolygon(yellow, new[] { new PointF(x + w * .2F, y + h * .42F), new PointF(x + w * .96F, y + h * .42F), new PointF(x + w * .84F, y + h * .85F), new PointF(x + w * .08F, y + h * .85F) });
                        }
                        break;
                    case CadGlyph.Save:
                        g.FillRectangle(blueFill, x + w * .12F, y + h * .1F, w * .76F, h * .8F);
                        g.FillRectangle(white, x + w * .28F, y + h * .1F, w * .44F, h * .28F);
                        g.FillRectangle(white, x + w * .24F, y + h * .55F, w * .52F, h * .3F);
                        break;
                    case CadGlyph.Delete:
                        g.DrawLine(red, x + w * .22F, y + h * .22F, x + w * .78F, y + h * .78F); g.DrawLine(red, x + w * .78F, y + h * .22F, x + w * .22F, y + h * .78F); break;
                    case CadGlyph.Up: Arrow(g, blue, r, true); break;
                    case CadGlyph.Down: Arrow(g, blue, r, false); break;
                    case CadGlyph.Settings:
                        using (var gear = new Pen(c(CadTheme.Text), 2.2F))
                        {
                            float cx = x + w / 2, cy = y + h / 2, rr = w * .3F;
                            for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; g.DrawLine(gear, cx + (float)Math.Cos(a) * rr * .8F, cy + (float)Math.Sin(a) * rr * .8F, cx + (float)Math.Cos(a) * rr * 1.45F, cy + (float)Math.Sin(a) * rr * 1.45F); }
                            g.DrawEllipse(gear, cx - rr, cy - rr, rr * 2, rr * 2);
                        }
                        break;
                    case CadGlyph.Generate:
                    {
                        g.FillRectangle(white, x + w * .2F, y + h * .08F, w * .6F, h * .84F);
                        using (var lines = new Pen(c(CadTheme.Primary), 1.6F)) for (int i = 0; i < 4; i++) g.DrawLine(lines, x + w * .32F, y + h * (.3F + i * .15F), x + w * .68F, y + h * (.3F + i * .15F));
                        break;
                    }
                    case CadGlyph.Search:
                        g.DrawEllipse(light, x + w * .15F, y + h * .15F, w * .5F, h * .5F); g.DrawLine(light, x + w * .58F, y + h * .58F, x + w * .85F, y + h * .85F); break;
                    case CadGlyph.InsertEnd:
                    case CadGlyph.InsertBelow:
                        using (var row = new Pen(c(CadTheme.Muted), 1.4F))
                        {
                            g.DrawRectangle(row, x + w * .12F, y + h * .14F, w * .76F, h * .22F);
                            if (glyph == CadGlyph.InsertBelow) g.DrawRectangle(row, x + w * .12F, y + h * .64F, w * .76F, h * .22F);
                            using (var dash = new Pen(c(CadTheme.IconBlue), 1.6F) { DashStyle = DashStyle.Dash })
                                g.DrawRectangle(dash, x + w * .12F, y + h * (glyph == CadGlyph.InsertBelow ? .39F : .5F), w * .76F, h * .2F);
                            g.DrawLine(blue, x + w * .5F, y + h * (glyph == CadGlyph.InsertBelow ? .42F : .53F), x + w * .5F, y + h * (glyph == CadGlyph.InsertBelow ? .56F : .67F));
                        }
                        break;
                    case CadGlyph.Reset:
                        g.DrawArc(light, x + w * .2F, y + h * .2F, w * .6F, h * .6F, 200, 270);
                        using (var head = new SolidBrush(c(CadTheme.Text))) g.FillPolygon(head, new[] { new PointF(x + w * .1F, y + h * .3F), new PointF(x + w * .36F, y + h * .32F), new PointF(x + w * .2F, y + h * .52F) });
                        break;
                    case CadGlyph.ZoomIn:
                    case CadGlyph.ZoomOut:
                        g.DrawEllipse(light, x + w * .15F, y + h * .15F, w * .5F, h * .5F); g.DrawLine(light, x + w * .58F, y + h * .58F, x + w * .85F, y + h * .85F);
                        g.DrawLine(light, x + w * .28F, y + h * .4F, x + w * .52F, y + h * .4F);
                        if (glyph == CadGlyph.ZoomIn) g.DrawLine(light, x + w * .4F, y + h * .28F, x + w * .4F, y + h * .52F);
                        break;
                    case CadGlyph.Fit:
                        g.DrawLines(light, new[] { new PointF(x + w * .15F, y + h * .4F), new PointF(x + w * .15F, y + h * .15F), new PointF(x + w * .4F, y + h * .15F) });
                        g.DrawLines(light, new[] { new PointF(x + w * .6F, y + h * .85F), new PointF(x + w * .85F, y + h * .85F), new PointF(x + w * .85F, y + h * .6F) });
                        break;
                    case CadGlyph.Template:
                        g.FillRectangle(white, x + w * .14F, y + h * .14F, w * .72F, h * .72F);
                        using (var grid = new Pen(c(CadTheme.Primary), 1.4F))
                        { g.DrawLine(grid, x + w * .14F, y + h * .38F, x + w * .86F, y + h * .38F); g.DrawLine(grid, x + w * .14F, y + h * .62F, x + w * .86F, y + h * .62F); g.DrawLine(grid, x + w * .4F, y + h * .14F, x + w * .4F, y + h * .86F); }
                        break;
                    case CadGlyph.Trash:
                        using (var can = new Pen(c(CadTheme.Text), 1.6F) { LineJoin = LineJoin.Round })
                        {
                            g.DrawLine(can, x + w * .18F, y + h * .26F, x + w * .82F, y + h * .26F);
                            g.DrawLine(can, x + w * .4F, y + h * .14F, x + w * .6F, y + h * .14F);
                            g.DrawPolygon(can, new[] { new PointF(x + w * .26F, y + h * .3F), new PointF(x + w * .74F, y + h * .3F), new PointF(x + w * .68F, y + h * .88F), new PointF(x + w * .32F, y + h * .88F) });
                            g.DrawLine(can, x + w * .44F, y + h * .42F, x + w * .44F, y + h * .76F); g.DrawLine(can, x + w * .56F, y + h * .42F, x + w * .56F, y + h * .76F);
                        }
                        break;
                    case CadGlyph.Dropdown:
                        using (var arrow = new SolidBrush(c(CadTheme.Muted))) g.FillPolygon(arrow, new[] { new PointF(x + w * .25F, y + h * .4F), new PointF(x + w * .75F, y + h * .4F), new PointF(x + w * .5F, y + h * .68F) });
                        break;
                    case CadGlyph.Help:
                        g.DrawEllipse(light, x + w * .12F, y + h * .12F, w * .76F, h * .76F);
                        TextRenderer.DrawText(g, "?", CadTheme.UiBold, r, c(CadTheme.Text), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                        break;
                }
            }
        }

        private static void Arrow(Graphics g, Pen pen, Rectangle r, bool up)
        {
            float cx = r.X + r.Width / 2F, top = r.Y + r.Height * .18F, bottom = r.Y + r.Height * .82F, half = r.Width * .26F;
            if (up) { g.DrawLine(pen, cx, bottom, cx, top); g.DrawLines(pen, new[] { new PointF(cx - half, top + half), new PointF(cx, top), new PointF(cx + half, top + half) }); }
            else { g.DrawLine(pen, cx, top, cx, bottom); g.DrawLines(pen, new[] { new PointF(cx - half, bottom - half), new PointF(cx, bottom), new PointF(cx + half, bottom - half) }); }
        }
    }

    /// <summary>아이콘(+글자) 버튼. Primary=true이면 파란 주 버튼입니다.</summary>
    public class CadIconButton : Button
    {
        private bool hover, down;
        public CadGlyph Glyph { get; set; }
        public bool Primary { get; set; }
        /// <summary>true면 오른쪽에 ▾를 그려 누르면 메뉴가 열린다는 것을 보여줍니다.</summary>
        public bool ShowsMenu { get; set; }

        public CadIconButton(CadGlyph glyph, string text = "")
        {
            Glyph = glyph; Text = text ?? ""; Font = CadTheme.Ui; Cursor = Cursors.Hand; Margin = new Padding(0, 0, 6, 0);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Size = string.IsNullOrEmpty(Text) ? new Size(CadTheme.ToolButton, CadTheme.ToolButton) : new Size(Math.Max(64, TextRenderer.MeasureText(Text, Font).Width + (glyph == CadGlyph.None ? 28 : 44)), CadTheme.ToolButton);
            AccessibleName = Text;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Color back = !Enabled ? (Primary ? Color.FromArgb(64, 78, 94) : CadTheme.Panel) : Primary ? (down ? CadTheme.Primary : hover ? CadTheme.PrimaryHover : CadTheme.Primary) : down ? CadTheme.Selected : hover ? CadTheme.ButtonHover : CadTheme.Button;
            g.Clear(Parent != null ? Parent.BackColor : CadTheme.Window);
            using (var brush = new SolidBrush(back)) g.FillRectangle(brush, 0, 0, Width - 1, Height - 1);
            using (var pen = new Pen(Primary ? CadTheme.SelectedBorder : Focused ? CadTheme.SelectedBorder : CadTheme.ButtonBorder)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            int icon = Math.Min(Height - CadTheme.Px(this, 12), CadTheme.Px(this, 20));
            bool textOnly = Glyph == CadGlyph.None;
            if (string.IsNullOrEmpty(Text)) { GlyphPainter.Draw(g, Glyph, new Rectangle((Width - icon) / 2, (Height - icon) / 2, icon, icon), Enabled); return; }
            int left = textOnly ? 0 : CadTheme.Px(this, 8), drop = CadTheme.Px(this, 12);
            if (ShowsMenu) GlyphPainter.Draw(g, CadGlyph.Dropdown, new Rectangle(Width - CadTheme.Px(this, 16), (Height - drop) / 2, drop, drop), Enabled);
            if (!textOnly) GlyphPainter.Draw(g, Glyph, new Rectangle(left, (Height - icon) / 2, icon, icon), Enabled);
            int arrow = ShowsMenu ? CadTheme.Px(this, 14) : 0;
            var area = textOnly ? new Rectangle(0, 0, Width - arrow, Height) : new Rectangle(left + icon + CadTheme.Px(this, 6), 0, Width - left - icon - CadTheme.Px(this, 10) - arrow, Height);
            TextRenderer.DrawText(g, Text, Primary ? CadTheme.BoldOf(this) : Font, area, Enabled ? CadTheme.Text : CadTheme.Disabled,
                (textOnly ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left) | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>위쪽 탭 줄(공사·유역·우수…). 선택 탭은 파란색, 항목이 있는 탭은 숫자 배지를 보여줍니다.</summary>
    public class CadTabStrip : Control
    {
        public sealed class Tab { public string Key; public string Text; public int Badge; public Rectangle Bounds; }
        private readonly List<Tab> tabs = new List<Tab>();
        private int hover = -1;
        public event EventHandler<string> TabClicked;
        public string SelectedKey { get; private set; }
        public IList<Tab> Tabs { get { return tabs; } }

        public CadTabStrip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Font = CadTheme.Ui; Height = 30; Cursor = Cursors.Hand; TabStop = true;
        }

        public void Add(string key, string text) { tabs.Add(new Tab { Key = key, Text = text }); Invalidate(); }
        public void Select(string key) { SelectedKey = key; Invalidate(); }
        public void SetBadge(string key, int badge) { var tab = tabs.Find(t => t.Key == key); if (tab != null && tab.Badge != badge) { tab.Badge = badge; Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Parent != null ? Parent.BackColor : CadTheme.Window);
            int x = 0, width = tabs.Count == 0 ? 0 : Math.Max(CadTheme.Px(this, 70), Math.Min(CadTheme.Px(this, 116), (Width - 1) / tabs.Count));
            var smallFont = CadTheme.SmallOf(this);
            for (int i = 0; i < tabs.Count; i++)
            {
                var tab = tabs[i]; tab.Bounds = new Rectangle(x, 0, width, Height - 1); x += width;
                bool selected = tab.Key == SelectedKey;
                using (var brush = new SolidBrush(selected ? CadTheme.Primary : i == hover ? CadTheme.ButtonHover : CadTheme.Header)) g.FillRectangle(brush, tab.Bounds);
                using (var pen = new Pen(selected ? CadTheme.SelectedBorder : CadTheme.PanelBorder)) g.DrawRectangle(pen, tab.Bounds);
                var textArea = tab.Bounds; if (tab.Badge > 0) textArea.Width -= CadTheme.Px(this, 16);
                TextRenderer.DrawText(g, tab.Text, selected ? CadTheme.BoldOf(this) : Font, textArea, CadTheme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                if (tab.Badge > 0)
                {
                    string badge = tab.Badge > 99 ? "99+" : tab.Badge.ToString();
                    int bh = Math.Max(CadTheme.Px(this, 17), TextRenderer.MeasureText(badge, smallFont).Height + 2);
                    int bw = Math.Max(CadTheme.Px(this, 18), TextRenderer.MeasureText(badge, smallFont).Width + CadTheme.Px(this, 6));
                    var b = new Rectangle(tab.Bounds.Right - bw - CadTheme.Px(this, 6), (Height - bh) / 2, bw, bh);
                    using (var brush = new SolidBrush(selected ? Color.FromArgb(40, 80, 122) : CadTheme.Selected)) g.FillRectangle(brush, b);
                    TextRenderer.DrawText(g, badge, smallFont, b, CadTheme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
            if (Focused && SelectedKey != null) { var s = tabs.Find(t => t.Key == SelectedKey); if (s != null) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(s.Bounds, -3, -3)); }
        }

        protected override void OnMouseMove(MouseEventArgs e) { int i = tabs.FindIndex(t => t.Bounds.Contains(e.Location)); if (i != hover) { hover = i; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e) { var tab = tabs.Find(t => t.Bounds.Contains(e.Location)); if (tab != null) Raise(tab.Key); base.OnMouseClick(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            int i = tabs.FindIndex(t => t.Key == SelectedKey);
            if (e.KeyCode == Keys.Left && i > 0) Raise(tabs[i - 1].Key);
            if (e.KeyCode == Keys.Right && i >= 0 && i < tabs.Count - 1) Raise(tabs[i + 1].Key);
            base.OnKeyDown(e);
        }
        /// <summary>코드에서 탭을 누른 것처럼 처리합니다.</summary>
        public void PerformClick(string key) { Raise(key); }
        private void Raise(string key) { var handler = TabClicked; if (handler != null) handler(this, key); }
    }

    /// <summary>제목이 있는 묶음 상자("원본 항목", "선택 항목 (6)", "CAD 미리보기").</summary>
    public class CadGroupPanel : Panel
    {
        private readonly Label title;
        private readonly FlowLayoutPanel tools;
        public Panel Content { get; private set; }
        public string Title { get { return title.Text; } set { title.Text = value; } }

        public CadGroupPanel(string text)
        {
            BackColor = CadTheme.Panel; Padding = new Padding(1); Margin = new Padding(0);
            var head = CadTheme.SingleRow(new TableLayoutPanel { Dock = DockStyle.Top, Height = 26, ColumnCount = 2, Margin = Padding.Empty, Padding = new Padding(8, 0, 4, 0) });
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            title = new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = CadTheme.Title, Margin = Padding.Empty, AutoEllipsis = true };
            tools = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 0), Anchor = AnchorStyles.Right };
            head.Controls.Add(title, 0, 0); head.Controls.Add(tools, 1, 0);
            Content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 0, 6, 6) };
            Controls.Add(Content); Controls.Add(head);
            CadTheme.Keep(this);
        }

        /// <summary>제목 줄 오른쪽에 작은 아이콘 버튼을 붙입니다.</summary>
        public CadIconButton AddTool(CadGlyph glyph, string name, string tip, ToolTip tips, EventHandler click)
        {
            var button = new CadIconButton(glyph) { Name = name, Size = new Size(22, 22), Margin = new Padding(3, 0, 0, 0) };
            button.Click += click; if (tips != null) tips.SetToolTip(button, tip); tools.Controls.Add(button); return button;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(CadTheme.PanelBorder)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    /// <summary>
    /// 테두리가 온전히 그려지는 입력칸. 오른쪽에 돋보기(검색)·▾(목록) 아이콘을 둘 수 있습니다.
    /// 안쪽 TextBox는 테두리 없이 세로 가운데에 놓이고, 바깥 테두리는 이 컨트롤이 직접 그립니다.
    /// </summary>
    public class CadTextField : Panel
    {
        private const int TrailingWidth = 24;
        private readonly CadGlyph trailing;
        public TextBox Box { get; private set; }
        /// <summary>▾를 누르면 보여줄 목록. 고르면 Text가 바뀝니다.</summary>
        public System.Collections.Generic.List<string> Choices { get; private set; }
        /// <summary>오른쪽 아이콘을 눌렀을 때(목록이 없을 때).</summary>
        public event EventHandler TrailingClick;

        public CadTextField(string placeholder = null, CadGlyph trailing = CadGlyph.None)
        {
            this.trailing = trailing; Choices = new System.Collections.Generic.List<string>();
            Height = 26; BackColor = CadTheme.Field; Margin = new Padding(0); CadTheme.Keep(this);
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            Box = new TextBox { BorderStyle = BorderStyle.None, BackColor = CadTheme.Field, ForeColor = CadTheme.Text, Font = CadTheme.Ui };
            CadTheme.Keep(Box);
            if (!string.IsNullOrEmpty(placeholder)) CadTheme.SetPlaceholder(Box, placeholder);
            Controls.Add(Box);
            Box.TextChanged += (s, e) => OnTextChanged(e);
            Box.GotFocus += (s, e) => Invalidate(); Box.LostFocus += (s, e) => Invalidate();
            Click += (s, e) => Box.Focus();
            Cursor = Cursors.IBeam;
        }

        public override string Text { get { return Box == null ? base.Text : Box.Text; } set { if (Box == null) base.Text = value; else Box.Text = value; } }
        public void SelectAll() { Box.SelectAll(); }
        public new bool Focus() { return Box.Focus(); }
        private bool HasTrailing { get { return trailing != CadGlyph.None; } }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Box == null) return;
            int width = Math.Max(10, Width - 12 - (HasTrailing ? TrailingWidth : 0));
            Box.SetBounds(6, Math.Max(1, (Height - Box.PreferredHeight) / 2 + 1), width, Box.PreferredHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(CadTheme.Field);
            bool focused = Box.Focused;
            if (HasTrailing)
            {
                var area = new Rectangle(Width - TrailingWidth, 0, TrailingWidth, Height);
                using (var line = new Pen(CadTheme.PanelBorder)) g.DrawLine(line, area.Left, 3, area.Left, Height - 4);
                int size = trailing == CadGlyph.Dropdown ? 12 : 16;
                GlyphPainter.Draw(g, trailing, new Rectangle(area.Left + (TrailingWidth - size) / 2, (Height - size) / 2, size, size), Enabled);
            }
            using (var pen = new Pen(focused ? CadTheme.SelectedBorder : CadTheme.ButtonBorder)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        protected override void OnMouseMove(MouseEventArgs e) { Cursor = HasTrailing && e.X >= Width - TrailingWidth ? Cursors.Hand : Cursors.IBeam; base.OnMouseMove(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (!HasTrailing || e.X < Width - TrailingWidth) { Box.Focus(); return; }
            if (Choices.Count > 0)
            {
                var menu = CadMenu.Create();
                foreach (var choice in Choices) { string value = choice; CadMenu.Add(menu, (value == Text ? "✓ " : "    ") + value, (s, a) => { Text = value; Box.Focus(); Box.SelectAll(); }); }
                menu.MinimumSize = new Size(Width, 0);
                menu.Closed += (s, a) => BeginInvoke((Action)menu.Dispose);
                CadMenu.Show(menu, this);
            }
            else { var handler = TrailingClick; if (handler != null) handler(this, EventArgs.Empty); else Box.Focus(); }
        }
    }

    /// <summary>돋보기 아이콘이 있는 검색칸.</summary>
    public class CadSearchBox : CadTextField
    {
        public CadSearchBox(string placeholder) : base(placeholder, CadGlyph.Search) { TrailingClick += (s, e) => Box.Focus(); }
    }
}

namespace CadUi
{
    /// <summary>어두운 테마 팝업 메뉴(프리셋 목록, 설정 메뉴 등).</summary>
    public static class CadMenu
    {
        public static ContextMenuStrip Create()
        {
            var menu = new ContextMenuStrip { Font = CadTheme.Ui, ShowImageMargin = false, Renderer = new ToolStripProfessionalRenderer(new Colors()) };
            menu.BackColor = CadTheme.Panel; menu.ForeColor = CadTheme.Text;
            return menu;
        }

        public static ToolStripItem Add(ContextMenuStrip menu, string text, EventHandler click, bool enabled = true)
        {
            var item = new ToolStripMenuItem(text) { ForeColor = CadTheme.Text, Enabled = enabled, Padding = new Padding(4, 3, 4, 3) };
            if (click != null) item.Click += click; menu.Items.Add(item); return item;
        }

        public static void AddHeader(ContextMenuStrip menu, string text)
        {
            menu.Items.Add(new ToolStripLabel(text) { ForeColor = CadTheme.Muted, Font = CadTheme.Small, Padding = new Padding(4, 4, 4, 0) });
        }

        public static void Show(ContextMenuStrip menu, Control anchor) { menu.Show(anchor, new System.Drawing.Point(0, anchor.Height)); }

        private sealed class Colors : ProfessionalColorTable
        {
            public override System.Drawing.Color ToolStripDropDownBackground { get { return CadTheme.Panel; } }
            public override System.Drawing.Color MenuBorder { get { return CadTheme.PanelBorder; } }
            public override System.Drawing.Color MenuItemBorder { get { return CadTheme.SelectedBorder; } }
            public override System.Drawing.Color MenuItemSelected { get { return CadTheme.Selected; } }
            public override System.Drawing.Color MenuItemSelectedGradientBegin { get { return CadTheme.Selected; } }
            public override System.Drawing.Color MenuItemSelectedGradientEnd { get { return CadTheme.Selected; } }
            public override System.Drawing.Color SeparatorDark { get { return CadTheme.PanelBorder; } }
            public override System.Drawing.Color SeparatorLight { get { return CadTheme.Panel; } }
            public override System.Drawing.Color ImageMarginGradientBegin { get { return CadTheme.Panel; } }
            public override System.Drawing.Color ImageMarginGradientMiddle { get { return CadTheme.Panel; } }
            public override System.Drawing.Color ImageMarginGradientEnd { get { return CadTheme.Panel; } }
        }
    }
}
