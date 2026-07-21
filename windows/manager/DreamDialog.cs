using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CodexDreamSkinManager
{
  internal enum DreamDialogTone
  {
    Information,
    Question,
    Warning,
    Danger
  }

  internal sealed class DreamDialog : Form
  {
    private static readonly Color Canvas = Color.FromArgb(247, 244, 239);
    private static readonly Color Surface = Color.FromArgb(255, 253, 249);
    private static readonly Color Ink = Color.FromArgb(60, 45, 48);
    private static readonly Color Muted = Color.FromArgb(103, 84, 88);
    private static readonly Color Border = Color.FromArgb(226, 213, 205);
    private static readonly Color Coral = Color.FromArgb(206, 103, 82);
    private static readonly Color Teal = Color.FromArgb(61, 132, 137);
    private static readonly Color Danger = Color.FromArgb(176, 61, 61);

    private DreamDialog(string title, string message, string primaryText, string secondaryText,
      DreamDialogTone tone, bool primaryDanger, bool defaultPrimary)
    {
      Text = title;
      try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
      Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
      BackColor = Canvas;
      ForeColor = Ink;
      StartPosition = FormStartPosition.CenterParent;
      FormBorderStyle = FormBorderStyle.FixedDialog;
      ShowInTaskbar = false;
      MaximizeBox = false;
      MinimizeBox = false;
      AutoScaleMode = AutoScaleMode.Dpi;

      Size measured = TextRenderer.MeasureText(message ?? string.Empty, Font, new Size(396, 0),
        TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
      int messageHeight = Math.Max(48, Math.Min(176, measured.Height + 8));
      ClientSize = new Size(520, 176 + messageHeight);

      DialogGlyph glyph = new DialogGlyph(tone)
      {
        Location = new Point(26, 24),
        Size = new Size(46, 46),
        AccessibleName = ToneName(tone)
      };
      Label titleLabel = new Label
      {
        AutoSize = false,
        AutoEllipsis = true,
        Text = title,
        Font = new Font("Segoe UI", 16F, FontStyle.Bold),
        ForeColor = Ink,
        BackColor = Color.Transparent,
        Location = new Point(88, 20),
        Size = new Size(402, 34)
      };
      Label messageLabel = new Label
      {
        AutoSize = false,
        Text = message ?? string.Empty,
        Font = new Font("Segoe UI", 10F),
        ForeColor = Muted,
        BackColor = Color.Transparent,
        Location = new Point(90, 60),
        Size = new Size(398, messageHeight),
        UseMnemonic = false
      };
      Panel divider = new Panel
      {
        BackColor = Border,
        Location = new Point(0, 84 + messageHeight),
        Size = new Size(ClientSize.Width, 1),
        Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
      };

      DreamButton primary = new DreamButton
      {
        Text = primaryText,
        Size = new Size(Math.Max(96, TextRenderer.MeasureText(primaryText, Font).Width + 32), 42),
        Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
        Cursor = Cursors.Hand,
        Primary = !primaryDanger,
        Danger = primaryDanger,
        DialogResult = DialogResult.Yes,
        AccessibleName = primaryText
      };
      DreamButton secondary = null;
      if (!string.IsNullOrEmpty(secondaryText))
      {
        secondary = new DreamButton
        {
          Text = secondaryText,
          Size = new Size(Math.Max(88, TextRenderer.MeasureText(secondaryText, Font).Width + 28), 42),
          Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
          Cursor = Cursors.Hand,
          DialogResult = DialogResult.No,
          AccessibleName = secondaryText
        };
      }

      int buttonTop = ClientSize.Height - 66;
      primary.Location = new Point(ClientSize.Width - primary.Width - 24, buttonTop);
      if (secondary != null)
        secondary.Location = new Point(primary.Left - secondary.Width - 10, buttonTop);

      Controls.Add(glyph);
      Controls.Add(titleLabel);
      Controls.Add(messageLabel);
      Controls.Add(divider);
      if (secondary != null) Controls.Add(secondary);
      Controls.Add(primary);

      AcceptButton = defaultPrimary || secondary == null ? primary : secondary;
      CancelButton = secondary ?? primary;
    }

    public static DialogResult Confirm(IWin32Window owner, string title, string message,
      string primaryText, string secondaryText, DreamDialogTone tone, bool primaryDanger, bool defaultPrimary)
    {
      using (var dialog = new DreamDialog(title, message, primaryText, secondaryText, tone, primaryDanger, defaultPrimary))
        return dialog.ShowDialog(owner);
    }

    public static void ShowMessage(IWin32Window owner, string title, string message, DreamDialogTone tone)
    {
      using (var dialog = new DreamDialog(title, message, "知道了", null, tone, false, true))
        dialog.ShowDialog(owner);
    }

    private static string ToneName(DreamDialogTone tone)
    {
      if (tone == DreamDialogTone.Danger) return "错误";
      if (tone == DreamDialogTone.Warning) return "警告";
      if (tone == DreamDialogTone.Question) return "确认";
      return "提示";
    }

    private sealed class DialogGlyph : Control
    {
      private readonly DreamDialogTone tone;

      public DialogGlyph(DreamDialogTone tone)
      {
        this.tone = tone;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
          ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
      }

      protected override void OnPaint(PaintEventArgs args)
      {
        args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Color color = tone == DreamDialogTone.Danger ? Danger :
          tone == DreamDialogTone.Warning ? Coral : Teal;
        using (var brush = new SolidBrush(Color.FromArgb(28, color))) args.Graphics.FillEllipse(brush, 0, 0, Width - 1, Height - 1);
        using (var pen = new Pen(color, 1.5F)) args.Graphics.DrawEllipse(pen, 1, 1, Width - 3, Height - 3);
        string glyph = tone == DreamDialogTone.Question ? "?" :
          tone == DreamDialogTone.Information ? "i" : "!";
        using (var font = new Font("Segoe UI", 17F, FontStyle.Bold))
          TextRenderer.DrawText(args.Graphics, glyph, font, ClientRectangle, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
      }
    }
  }
}
