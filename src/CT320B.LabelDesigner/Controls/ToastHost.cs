using CT320B.LabelDesigner.Services;

namespace CT320B.LabelDesigner.Controls;

/// <summary>
/// A non-modal toast overlay (Phase 14a): transient cards that stack up from the bottom-right and
/// auto-dismiss, replacing blocking message boxes for routine successes/warnings/errors. Add it to the
/// form last and call <see cref="BringToFront"/> so it floats over the docked content; <see cref="Show"/>
/// pops a card (click a card to dismiss it early).
/// </summary>
public sealed class ToastHost : FlowLayoutPanel
{
    public ToastHost()
    {
        FlowDirection = FlowDirection.BottomUp;
        WrapContents = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Color.Transparent;
        Padding = new Padding(0);
        Margin = new Padding(0);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Reposition();   // grow up/left, keeping the bottom-right corner pinned
    }

    /// <summary>Pops a toast for the given severity/message. Errors linger longer than info/success.</summary>
    public void Show(LogSeverity severity, string message)
    {
        if (IsDisposed) return;
        var card = new ToastCard(severity, message);
        card.Dismissed += c => RemoveCard(c);
        Controls.Add(card);
        Reposition();
    }

    private void RemoveCard(ToastCard card)
    {
        if (Controls.Contains(card)) Controls.Remove(card);
        card.Dispose();
        Reposition();
    }

    // Keep the stack pinned to the parent's bottom-right with a small inset.
    public void Reposition()
    {
        if (Parent is null) return;
        Location = new Point(Parent.ClientSize.Width - Width - 16, Parent.ClientSize.Height - Height - 16);
    }

    private sealed class ToastCard : Panel
    {
        public event Action<ToastCard>? Dismissed;
        private readonly System.Windows.Forms.Timer _timer;

        public ToastCard(LogSeverity severity, string message)
        {
            Width = 320;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Margin = new Padding(0, 6, 0, 0);
            Padding = new Padding(10, 8, 10, 8);
            (Color back, Color fore) = Colours(severity);
            BackColor = back;
            Cursor = Cursors.Hand;

            var bar = new Panel { Dock = DockStyle.Left, Width = 4, BackColor = Accent(severity) };
            var text = new Label
            {
                Text = message, ForeColor = fore, AutoSize = true, MaximumSize = new Size(286, 0),
                Font = SystemFonts.MessageBoxFont, Dock = DockStyle.Fill, Padding = new Padding(8, 0, 0, 0),
            };
            Controls.Add(text);
            Controls.Add(bar);
            Click += (_, _) => Dismiss();
            text.Click += (_, _) => Dismiss();

            _timer = new System.Windows.Forms.Timer { Interval = severity == LogSeverity.Error ? 8000 : 4500 };
            _timer.Tick += (_, _) => Dismiss();
            _timer.Start();
        }

        private void Dismiss()
        {
            _timer.Stop();
            Dismissed?.Invoke(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }

        private static (Color, Color) Colours(LogSeverity s) => s switch
        {
            LogSeverity.Success => (Color.FromArgb(232, 245, 233), Color.FromArgb(27, 94, 32)),
            LogSeverity.Warning => (Color.FromArgb(255, 248, 225), Color.FromArgb(120, 80, 0)),
            LogSeverity.Error => (Color.FromArgb(253, 236, 234), Color.FromArgb(150, 16, 16)),
            _ => (Color.FromArgb(232, 240, 254), Color.FromArgb(20, 60, 130)),
        };

        private static Color Accent(LogSeverity s) => s switch
        {
            LogSeverity.Success => Color.FromArgb(46, 160, 67),
            LogSeverity.Warning => Color.FromArgb(220, 160, 0),
            LogSeverity.Error => Color.FromArgb(200, 40, 40),
            _ => Color.FromArgb(40, 110, 220),
        };
    }
}
