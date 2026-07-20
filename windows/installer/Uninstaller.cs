using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Codex皮肤主题管理器卸载程序")]
[assembly: AssemblyDescription("Uninstall Codex皮肤主题管理器")]
[assembly: AssemblyCompany("Codex Dream Skin")]
[assembly: AssemblyProduct("Codex皮肤主题管理器卸载程序")]
[assembly: AssemblyVersion("2.3.3.0")]
[assembly: AssemblyFileVersion("2.3.3.0")]

namespace CodexDreamSkinUninstaller
{
  internal static class Program
  {
    private const string ProductKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\CodexDreamSkinManager";
    private const string ProductFolder = "codex-skin-manager";

    [STAThread]
    private static int Main(string[] args)
    {
      try
      {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length > 0 && string.Equals(args[0], "--remove", StringComparison.OrdinalIgnoreCase))
          return Remove(args.Length > 1 ? args[1] : null, args.Length > 2 ? args[2] : null);
        if (args.Length > 0 && string.Equals(args[0], "--test-root", StringComparison.OrdinalIgnoreCase))
          return RemoveTestRoot(args.Length > 1 ? args[1] : null);
        string root = Path.GetFullPath(Path.GetDirectoryName(Application.ExecutablePath));
        if (!IsAllowedInstallRoot(root)) throw new InvalidOperationException("卸载目录不是有效的 Codex Dream Skin 安装目录。");
        DialogResult answer = DreamDialog.ShowMessage(null,
          "将删除皮肤管理器程序和快捷方式。\n\nskin 文件夹、已导入皮肤和活动配置会保留。\n\n确定继续吗？",
          "卸载 Codex皮肤主题管理器", MessageBoxIcon.Warning, MessageBoxButtons.YesNo, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return 0;
        string temp = Path.Combine(Path.GetTempPath(), "CodexDreamSkinUninstall-" + Guid.NewGuid().ToString("N") + ".exe");
        File.Copy(Application.ExecutablePath, temp, true);
        Process.Start(new ProcessStartInfo(temp, "--remove \"" + root + "\" \"" + temp + "\"") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
        return 0;
      }
      catch (Exception error)
      {
        DreamDialog.ShowMessage(null, error.Message, "卸载失败", MessageBoxIcon.Error,
          MessageBoxButtons.OK, MessageBoxDefaultButton.Button1);
        return 1;
      }
    }

    private static int Remove(string root, string self)
    {
      if (string.IsNullOrWhiteSpace(root) || !IsAllowedInstallRoot(root)) return 1;
      foreach (string processName in new[] { "Codex皮肤主题管理器", "Codex Dream Skin Manager" })
      {
        foreach (Process process in Process.GetProcessesByName(processName))
        {
          try
          {
            string path = process.MainModule == null ? null : process.MainModule.FileName;
            if (string.Equals(path, Path.Combine(root, "Codex皮肤主题管理器.exe"), StringComparison.OrdinalIgnoreCase) ||
              string.Equals(path, Path.Combine(root, "Codex Dream Skin Manager.exe"), StringComparison.OrdinalIgnoreCase)) process.Kill();
          }
          catch { }
          finally { process.Dispose(); }
        }
      }
      RemoveShortcuts();
      using (RegistryKey key = Registry.CurrentUser.OpenSubKey(ProductKey, true))
      {
        if (key != null) Registry.CurrentUser.DeleteSubKeyTree(ProductKey, false);
      }
      RemoveProgramFiles(root);
      if (!string.IsNullOrWhiteSpace(self)) ScheduleDelete(self);
      return 0;
    }

    private static int RemoveTestRoot(string root)
    {
      if (string.IsNullOrWhiteSpace(root)) return 1;
      RemoveProgramFiles(root);
      return 0;
    }

    private static void RemoveProgramFiles(string root)
    {
      if (!Directory.Exists(root)) return;
      string engine = Path.Combine(root, ".codex-dream-skin");
      try { if (Directory.Exists(engine)) Directory.Delete(engine, true); } catch { }
      foreach (string name in new[]
      {
        "Codex皮肤主题管理器.exe", "Codex皮肤主题管理器卸载程序.exe",
        "Codex Dream Skin Manager.exe", "Codex Dream Skin Uninstaller.exe", "VERSION", "README.txt"
      })
      {
        string path = Path.Combine(root, name);
        try { if (File.Exists(path)) File.Delete(path); } catch { ScheduleDelete(path); }
      }
      try
      {
        if (Directory.Exists(root) && Directory.GetFileSystemEntries(root).Length == 0) Directory.Delete(root);
      }
      catch { }
    }

    private static void RemoveShortcuts()
    {
      string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
      string start = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs");
      foreach (string path in new[]
      {
        Path.Combine(desktop, "Codex皮肤主题管理器.lnk"), Path.Combine(start, "Codex皮肤主题管理器.lnk"),
        Path.Combine(desktop, "Codex Dream Skin Manager.lnk"), Path.Combine(start, "Codex Dream Skin Manager.lnk")
      })
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static bool IsAllowedInstallRoot(string root)
    {
      string fullRoot = Path.GetFullPath(root).TrimEnd('\\');
      if (!string.Equals(Path.GetFileName(fullRoot), ProductFolder, StringComparison.OrdinalIgnoreCase)) return false;
      try
      {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(ProductKey))
        {
          object value = key == null ? null : key.GetValue("InstallLocation");
          if (value == null) return false;
          string registered = Path.GetFullPath(Convert.ToString(value)).TrimEnd('\\');
          return string.Equals(fullRoot, registered, StringComparison.OrdinalIgnoreCase);
        }
      }
      catch { return false; }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string existingFileName, string newFileName, int flags);

    private static void ScheduleDelete(string path)
    {
      MoveFileEx(path, null, 4);
    }
  }

  internal static class DreamDialogPalette
  {
    public static readonly Color Canvas = Color.FromArgb(248, 242, 238);
    public static readonly Color Ink = Color.FromArgb(58, 42, 48);
    public static readonly Color Muted = Color.FromArgb(116, 91, 98);
    public static readonly Color Border = Color.FromArgb(226, 205, 196);
    public static readonly Color Coral = Color.FromArgb(206, 103, 82);
    public static readonly Color Teal = Color.FromArgb(61, 132, 137);
    public static readonly Color Violet = Color.FromArgb(137, 95, 174);
    public static readonly Color Danger = Color.FromArgb(176, 61, 61);
  }

  internal static class DreamDialogGeometry
  {
    public static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
      int diameter = Math.Max(1, radius * 2);
      GraphicsPath path = new GraphicsPath();
      path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
      path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
      path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
      path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
      path.CloseFigure();
      return path;
    }
  }

  internal sealed class DreamDialog : Form
  {
    public static DialogResult ShowMessage(IWin32Window owner, string message, string title, MessageBoxIcon icon,
      MessageBoxButtons buttons, MessageBoxDefaultButton defaultButton)
    {
      using (DreamDialog dialog = new DreamDialog(title, message, icon, buttons, defaultButton))
      {
        return owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
      }
    }

    private DreamDialog(string title, string message, MessageBoxIcon icon, MessageBoxButtons buttons, MessageBoxDefaultButton defaultButton)
    {
      Text = title;
      try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
      StartPosition = FormStartPosition.CenterScreen;
      FormBorderStyle = FormBorderStyle.FixedDialog;
      ShowInTaskbar = false;
      MaximizeBox = false;
      MinimizeBox = false;
      BackColor = DreamDialogPalette.Canvas;
      Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
      AutoScaleMode = AutoScaleMode.Dpi;
      int bodyHeight = Math.Max(86, Math.Min(190, TextRenderer.MeasureText(message, Font, new Size(396, 0),
        TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height + 8));
      ClientSize = new Size(536, bodyHeight + 130);
      DoubleBuffered = true;

      GlowPanel glow = new GlowPanel { Dock = DockStyle.Fill };
      Controls.Add(glow);
      DreamDialogGlyph glyph = new DreamDialogGlyph(icon) { Location = new Point(30, 28), Size = new Size(58, 58), BackColor = DreamDialogPalette.Canvas };
      Label titleLabel = new Label { AutoSize = false, Text = title, Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold), ForeColor = DreamDialogPalette.Ink, BackColor = Color.Transparent, Location = new Point(104, 30), Size = new Size(398, 32) };
      Label body = new Label { AutoSize = false, Text = message, Font = new Font("Microsoft YaHei UI", 9.4F), ForeColor = DreamDialogPalette.Muted, BackColor = Color.Transparent, Location = new Point(106, 68), Size = new Size(398, bodyHeight) };
      glow.Controls.Add(glyph);
      glow.Controls.Add(titleLabel);
      glow.Controls.Add(body);
      AddButtons(glow, buttons, defaultButton, bodyHeight + 86);
    }

    private void AddButtons(Control host, MessageBoxButtons buttons, MessageBoxDefaultButton defaultButton, int top)
    {
      if (buttons == MessageBoxButtons.YesNo)
      {
        DreamButton no = CreateButton("取消", DialogResult.No, false, false, new Point(ClientSize.Width - 218, top));
        DreamButton yes = CreateButton("继续", DialogResult.Yes, true, false, new Point(ClientSize.Width - 118, top));
        host.Controls.Add(no);
        host.Controls.Add(yes);
        AcceptButton = defaultButton == MessageBoxDefaultButton.Button2 ? no : yes;
        CancelButton = no;
        return;
      }
      DreamButton ok = CreateButton("知道了", DialogResult.OK, true, false, new Point(ClientSize.Width - 118, top));
      host.Controls.Add(ok);
      AcceptButton = ok;
      CancelButton = ok;
    }

    private DreamButton CreateButton(string text, DialogResult result, bool primary, bool danger, Point location)
    {
      return new DreamButton { Text = text, DialogResult = result, Primary = primary, Danger = danger, Size = new Size(88, 40), Location = location, BackColor = DreamDialogPalette.Canvas };
    }
  }

  internal sealed class GlowPanel : Panel
  {
    public GlowPanel()
    {
      SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    protected override void OnPaintBackground(PaintEventArgs args)
    {
      args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
      using (var gradient = new LinearGradientBrush(ClientRectangle,
        Color.FromArgb(255, 249, 244), Color.FromArgb(235, 247, 245), LinearGradientMode.Horizontal))
      {
        args.Graphics.FillRectangle(gradient, ClientRectangle);
      }
      using (var warm = new SolidBrush(Color.FromArgb(72, 234, 145, 119))) args.Graphics.FillEllipse(warm, Width - 260, -110, 280, 220);
      using (var cool = new SolidBrush(Color.FromArgb(42, 89, 164, 168))) args.Graphics.FillEllipse(cool, Width - 110, 44, 180, 160);
    }
  }

  internal sealed class DreamButton : Button
  {
    private bool hovered;
    private bool pressed;
    public bool Primary { get; set; }
    public bool Danger { get; set; }

    public DreamButton()
    {
      SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
      FlatStyle = FlatStyle.Flat;
      FlatAppearance.BorderSize = 0;
      UseVisualStyleBackColor = false;
      Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
      Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs eventArgs) { hovered = true; Invalidate(); base.OnMouseEnter(eventArgs); }
    protected override void OnMouseLeave(EventArgs eventArgs) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(eventArgs); }
    protected override void OnMouseDown(MouseEventArgs eventArgs) { pressed = true; Invalidate(); base.OnMouseDown(eventArgs); }
    protected override void OnMouseUp(MouseEventArgs eventArgs) { pressed = false; Invalidate(); base.OnMouseUp(eventArgs); }
    protected override void OnGotFocus(EventArgs eventArgs) { Invalidate(); base.OnGotFocus(eventArgs); }
    protected override void OnLostFocus(EventArgs eventArgs) { Invalidate(); base.OnLostFocus(eventArgs); }

    protected override void OnPaint(PaintEventArgs args)
    {
      args.Graphics.Clear(BackColor);
      args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
      Rectangle bounds = new Rectangle(1, 1, Width - 3, Height - 3);
      Color fill;
      Color border;
      Color text;
      if (Primary)
      {
        fill = pressed ? Color.FromArgb(164, 67, 54) : hovered ? Color.FromArgb(190, 84, 67) : DreamDialogPalette.Coral;
        border = fill;
        text = Color.White;
      }
      else
      {
        fill = pressed ? Color.FromArgb(238, 228, 222) : hovered ? Color.FromArgb(248, 238, 232) : Color.FromArgb(255, 253, 249);
        border = Danger ? Color.FromArgb(218, 156, 149) : DreamDialogPalette.Border;
        text = Danger ? DreamDialogPalette.Danger : DreamDialogPalette.Ink;
      }
      using (GraphicsPath path = DreamDialogGeometry.Rounded(bounds, 12))
      using (var brush = new SolidBrush(fill))
      using (var pen = new Pen(border))
      {
        args.Graphics.FillPath(brush, path);
        args.Graphics.DrawPath(pen, path);
      }
      TextRenderer.DrawText(args.Graphics, Text, Font, bounds, text,
        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
  }

  internal sealed class DreamDialogGlyph : Control
  {
    private readonly MessageBoxIcon icon;

    public DreamDialogGlyph(MessageBoxIcon icon)
    {
      this.icon = icon;
      SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    protected override void OnPaint(PaintEventArgs args)
    {
      args.Graphics.Clear(BackColor);
      args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
      Color accent = DreamDialogPalette.Teal;
      string symbol = "✓";
      if (icon == MessageBoxIcon.Error) { accent = DreamDialogPalette.Danger; symbol = "×"; }
      else if (icon == MessageBoxIcon.Warning) { accent = DreamDialogPalette.Coral; symbol = "!"; }
      else if (icon == MessageBoxIcon.Question) { accent = DreamDialogPalette.Violet; symbol = "?"; }
      using (var brush = new SolidBrush(Color.FromArgb(38, accent))) args.Graphics.FillEllipse(brush, 1, 1, Width - 3, Height - 3);
      using (var pen = new Pen(Color.FromArgb(140, accent), 1.2F)) args.Graphics.DrawEllipse(pen, 1, 1, Width - 3, Height - 3);
      using (var font = new Font("Microsoft YaHei UI", 21F, FontStyle.Bold))
      {
        TextRenderer.DrawText(args.Graphics, symbol, font, new Rectangle(0, 0, Width, Height), accent,
          TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
      }
    }
  }
}
