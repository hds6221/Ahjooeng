using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using CadUi;

namespace StormLegend
{
    /// <summary>원본 항목 목록의 열 너비(머리글과 각 행이 같은 값을 씁니다).</summary>
    internal static class ItemColumns
    {
        /// <summary>96 DPI 기준 픽셀. 화면 배율(125~200%)에서는 Px로 키워 씁니다.</summary>
        public const int Symbol = 96, Check = 24, RowHeight = 34;
        public static int Px(Control control, int value) { return (int)Math.Round(value * (control.DeviceDpi > 0 ? control.DeviceDpi : 96) / 96.0); }
        public static int NameWidth(int width, int symbol, int check) { return (int)((width - symbol - check) * .5); }
    }

    /// <summary>원본 항목 목록 머리글: 기호 | 명칭 | 규격.</summary>
    internal sealed class ItemListHeader : Control
    {
        public ItemListHeader() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); Height = 26; Font = CadTheme.Ui; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(CadTheme.Header);
            int sym = ItemColumns.Px(this, ItemColumns.Symbol), chk = ItemColumns.Px(this, ItemColumns.Check);
            int name = ItemColumns.NameWidth(Width - SystemInformation.VerticalScrollBarWidth, sym, chk);
            const TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(g, "기호", Font, new Rectangle(10, 0, sym, Height), CadTheme.Text, flags);
            TextRenderer.DrawText(g, "명칭", Font, new Rectangle(sym + 8, 0, name, Height), CadTheme.Text, flags);
            TextRenderer.DrawText(g, "규격", Font, new Rectangle(sym + name + 10, 0, Math.Max(10, Width - name - sym), Height), CadTheme.Text, flags);
            using (var pen = new Pen(CadTheme.GridLine))
            { g.DrawLine(pen, sym, 0, sym, Height); g.DrawLine(pen, 0, Height - 1, Width, Height - 1); }
        }
    }

    /// <summary>원본 항목 한 줄: [기호] 명칭 규격 ✓. 클릭이나 스페이스로 넣고 뺍니다.</summary>
    internal sealed class FacilityCard : Button
    {
        private readonly PreviewCatalog catalog;
        private Bitmap symbol;
        private bool picked, hover;
        public LegendRow Row { get; private set; }
        public bool Picked { get { return picked; } set { if (picked != value) { picked = value; Invalidate(); } } }

        public FacilityCard(LegendRow row, PreviewCatalog catalog)
        {
            Row = row; this.catalog = catalog;
            Name = "Card_" + row.Id; Text = row.Name + " " + row.Spec + " " + row.Remark;
            AccessibleName = Text; AccessibleDescription = "클릭 또는 스페이스 키로 이 항목을 넣거나 뺍니다.";
            Size = new Size(300, ItemColumns.RowHeight); Margin = Padding.Empty;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat; Cursor = Cursors.Hand; Font = CadTheme.Ui;
        }

        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); DisposeSymbol(); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void Dispose(bool disposing) { if (disposing) DisposeSymbol(); base.Dispose(disposing); }
        private void DisposeSymbol() { if (symbol != null) { symbol.Dispose(); symbol = null; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            int sym = ItemColumns.Px(this, ItemColumns.Symbol), chk = ItemColumns.Px(this, ItemColumns.Check);
            g.Clear(Picked ? CadTheme.Selected : hover ? CadTheme.ButtonHover : CadTheme.Field);
            int name = ItemColumns.NameWidth(Width, sym, chk);
            using (var pen = new Pen(CadTheme.GridLine))
            { g.DrawLine(pen, 0, Height - 1, Width, Height - 1); g.DrawLine(pen, sym, 0, sym, Height); }
            if (symbol == null) symbol = catalog.RenderSymbol(Row, sym - ItemColumns.Px(this, 8), Math.Max(8, Height - ItemColumns.Px(this, 6)));
            g.DrawImageUnscaled(symbol, ItemColumns.Px(this, 4), ItemColumns.Px(this, 3));
            const TextFormatFlags line = TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(g, Row.Name, Font, new Rectangle(sym + 8, 0, name - 4, Height), CadTheme.Text, line);
            string spec = Row.Spec + (string.IsNullOrEmpty(Row.Remark) ? "" : " · " + Row.Remark);
            TextRenderer.DrawText(g, spec, Font, new Rectangle(sym + name + 10, 0, Math.Max(10, Width - sym - name - chk - 12), Height), Picked ? CadTheme.Text : CadTheme.Muted, line);
            if (Picked)
            {
                TextRenderer.DrawText(g, "✓", CadTheme.UiBold, new Rectangle(Width - chk, 0, chk - 4, Height), CadTheme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                using (var bar = new SolidBrush(CadTheme.SelectedBorder)) g.FillRectangle(bar, 0, 0, 3, Height - 1);
            }
            if (Focused) ControlPaint.DrawFocusRectangle(g, new Rectangle(1, 1, Width - 2, Height - 3), CadTheme.Text, Picked ? CadTheme.Selected : CadTheme.Field);
        }
    }

    /// <summary>
    /// 기호 크게 보기: 목록이나 표의 기호 위에 마우스를 올리면 옆에 큰 그림(명칭·규격 포함)을 띄웁니다.
    /// 창 포커스를 가져가지 않아 클릭·입력은 그대로 됩니다.
    /// </summary>
    internal sealed class SymbolZoom : IDisposable
    {
        public const int ImageWidth = 300, ImageHeight = 132;
        private readonly PreviewCatalog catalog;
        private readonly Timer delay = new Timer { Interval = 300 };
        private readonly Dictionary<string, Bitmap> images = new Dictionary<string, Bitmap>();
        private ZoomWindow window;
        private LegendRow pending; private Control anchor; private Rectangle area; private string hint;

        public SymbolZoom(PreviewCatalog catalog)
        {
            this.catalog = catalog;
            delay.Tick += (s, e) => { delay.Stop(); Show(); };
        }

        /// <summary>지금 크게 보여 주는 항목(없으면 null).</summary>
        public LegendRow Current { get { return window != null && window.Visible ? window.Row : null; } }

        /// <summary>area(anchor 기준 좌표) 옆에 잠시 뒤 큰 그림을 띄웁니다. 이미 떠 있으면 바로 바꿉니다.</summary>
        public void Hover(Control anchor, Rectangle area, LegendRow row, string hint)
        {
            if (row == null || row.IsBlank) { Hide(); return; }
            if (window != null && window.Visible && window.Row == row) return;
            this.anchor = anchor; this.area = area; this.hint = hint; pending = row;
            if (window != null && window.Visible) Show(); else { delay.Stop(); delay.Start(); }
        }

        public void Hide() { delay.Stop(); pending = null; if (window != null && !window.IsDisposed && window.Visible) window.Hide(); }

        /// <summary>검사용: 기다리지 않고 바로 띄웁니다.</summary>
        internal void ShowNow() { delay.Stop(); Show(); }

        private void Show()
        {
            if (pending == null || anchor == null || anchor.IsDisposed || !anchor.IsHandleCreated) return;
            Bitmap image;
            if (!images.TryGetValue(pending.Id, out image)) { image = catalog.RenderSymbol(pending, ImageWidth, ImageHeight); images.Add(pending.Id, image); }
            if (window == null || window.IsDisposed) window = new ZoomWindow();
            window.Set(pending, image, hint);
            var screen = Screen.FromControl(anchor).WorkingArea;
            var corner = anchor.PointToScreen(new Point(area.Right + 8, area.Top + area.Height / 2 - window.Height / 2));
            if (corner.X + window.Width > screen.Right) corner.X = anchor.PointToScreen(new Point(area.Left - 8 - window.Width, 0)).X;
            corner.X = Math.Max(screen.Left, corner.X);
            corner.Y = Math.Max(screen.Top, Math.Min(corner.Y, screen.Bottom - window.Height));
            window.Location = corner;
            if (!window.Visible) window.Show(anchor.FindForm()); else window.Invalidate();
        }

        public void Dispose()
        {
            delay.Dispose();
            if (window != null) window.Dispose();
            foreach (var image in images.Values) image.Dispose();
            images.Clear();
        }

        private sealed class ZoomWindow : Form
        {
            private Bitmap image; private string hint;
            public LegendRow Row { get; private set; }
            public ZoomWindow()
            {
                FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; ControlBox = false;
                BackColor = CadTheme.Panel; Font = CadTheme.Ui; Name = "SymbolZoom";
                Size = new Size(ImageWidth + 20, ImageHeight + 72);
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams
            {
                get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x00000080; return p; } // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
            }
            public void Set(LegendRow row, Bitmap image, string hint) { Row = row; this.image = image; this.hint = hint; Invalidate(); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.Clear(CadTheme.Panel);
                using (var pen = new Pen(CadTheme.SelectedBorder)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                if (image != null) g.DrawImageUnscaled(image, 10, 10);
                using (var pen = new Pen(CadTheme.GridLine)) g.DrawRectangle(pen, 9, 9, ImageWidth + 1, ImageHeight + 1);
                if (Row == null) return;
                const TextFormatFlags line = TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
                string spec = Row.Spec + (string.IsNullOrEmpty(Row.Unit) ? "" : " · 단위 " + Row.Unit) + (string.IsNullOrEmpty(Row.Remark) ? "" : " · " + Row.Remark);
                TextRenderer.DrawText(g, Row.Name, CadTheme.UiBold, new Rectangle(10, ImageHeight + 16, ImageWidth, 20), CadTheme.Text, line);
                TextRenderer.DrawText(g, spec, Font, new Rectangle(10, ImageHeight + 35, ImageWidth, 18), CadTheme.Muted, line);
                if (!string.IsNullOrEmpty(hint)) TextRenderer.DrawText(g, hint, CadTheme.Small, new Rectangle(10, ImageHeight + 53, ImageWidth, 16), CadTheme.Disabled, line);
            }
        }
    }

    /// <summary>CAD 미리보기(흰 종이). 확대·축소·맞춤.</summary>
    internal sealed class LegendPreviewPanel : Panel
    {
        private readonly PreviewCanvas canvas;
        private readonly TemplateManifest manifest;
        private float zoom = 1;
        private bool fit = true;

        public LegendPreviewPanel(TemplateManifest manifest, PreviewCatalog catalog)
        {
            this.manifest = manifest; AutoScroll = true; BackColor = CadTheme.Paper; CadTheme.Keep(this);
            canvas = new PreviewCanvas(manifest, catalog) { Location = new Point(6, 6) }; Controls.Add(canvas);
            Resize += (s, e) => ResizeCanvas();
        }

        private float FrameWidth { get { return (float)manifest.FrameWidth; } }
        public void SetSelection(IList<LegendSelection> selection) { canvas.Selection = selection ?? new List<LegendSelection>(); ResizeCanvas(); canvas.Invalidate(); }
        public void Zoom(float factor)
        {
            if (fit) zoom = canvas.ViewScale / Math.Max(.005F, (ClientSize.Width - 16) / FrameWidth);
            fit = false; zoom = Math.Max(.3F, Math.Min(5F, zoom * factor)); ResizeCanvas(); canvas.Invalidate();
        }
        public void Fit() { fit = true; zoom = 1; AutoScrollPosition = Point.Empty; ResizeCanvas(); canvas.Invalidate(); }

        private void ResizeCanvas()
        {
            if (canvas.Selection.Count == 0)
            {
                AutoScrollPosition = Point.Empty; canvas.Location = new Point(6, 6);
                canvas.Size = new Size(Math.Max(80, ClientSize.Width - 12), Math.Max(40, ClientSize.Height - 12)); canvas.Invalidate(); return;
            }
            double height = manifest.Origin.Y - manifest.RowsTopY + canvas.Selection.Sum(s => s.IsBlank ? manifest.RowHeight : (manifest.FindRow(s.ItemId) ?? new LegendRow { Height = manifest.RowHeight }).Height) + manifest.BottomMargin;
            float scale = Math.Max(.005F, (ClientSize.Width - 16) / FrameWidth);
            if (fit) scale = Math.Min(scale, Math.Max(.005F, (float)((ClientSize.Height - 16) / height)));
            else scale *= zoom;
            canvas.ViewScale = scale;
            canvas.Size = new Size(Math.Max(60, (int)Math.Ceiling(FrameWidth * scale) + 12), Math.Max(40, (int)Math.Ceiling(height * scale) + 12));
            canvas.Location = fit ? new Point(Math.Max(2, (ClientSize.Width - canvas.Width) / 2), Math.Max(2, (ClientSize.Height - canvas.Height) / 2)) : new Point(2, 2);
        }

        private sealed class PreviewCanvas : Control
        {
            private readonly TemplateManifest manifest;
            private readonly PreviewCatalog catalog;
            public IList<LegendSelection> Selection = new List<LegendSelection>();
            public float ViewScale;
            public PreviewCanvas(TemplateManifest manifest, PreviewCatalog catalog)
            { this.manifest = manifest; this.catalog = catalog; DoubleBuffered = true; BackColor = CadTheme.Paper; Font = CadTheme.Ui; }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                if (catalog.UnavailableReason != null) { TextRenderer.DrawText(e.Graphics, "미리보기를 쓸 수 없습니다\r\n" + catalog.UnavailableReason + "\r\n\r\n항목 선택과 [범례 생성]은 그대로 됩니다.", Font, Rectangle.Inflate(ClientRectangle, -8, -8), CadTheme.Disabled, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak); return; }
                if (Selection.Count == 0) { TextRenderer.DrawText(e.Graphics, "왼쪽에서 항목을 고르면\r\n범례 모양이 보입니다", Font, ClientRectangle, CadTheme.Disabled, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak); return; }
                catalog.DrawLegend(e.Graphics, manifest, Selection, ViewScale, 6, 6);
            }
        }
    }
}
