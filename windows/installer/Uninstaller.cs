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
[assembly: AssemblyVersion("2.5.4.0")]
[assembly: AssemblyFileVersion("2.5.4.0")]

namespace CodexDreamSkinUninstaller
{
  internal sealed class UninstallDialog : Form
  {
    private static readonly Color Canvas = Color.FromArgb(247, 244, 239);
    private static readonly Color Surface = Color.FromArgb(255, 253, 249);
    private static readonly Color Ink = Color.FromArgb(60, 45, 48);
    private static readonly Color Muted = Color.FromArgb(105, 87, 91);
    private static readonly Color Border = Color.FromArgb(226, 213, 205);
    private static readonly Color Coral = Color.FromArgb(206, 103, 82);
    private static readonly Color Teal = Color.FromArgb(61, 132, 137);
    private static readonly Color Danger = Color.FromArgb(176, 61, 61);

    private readonly string screenshotPath;

    private UninstallDialog(string title, string message, bool confirmation, string screenshotPath)
    {
      this.screenshotPath = screenshotPath;
      Text = title;
      try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
      StartPosition = FormStartPosition.CenterScreen;
      FormBorderStyle = FormBorderStyle.FixedDialog;
      ShowInTaskbar = false;
      MaximizeBox = false;
      MinimizeBox = false;
      BackColor = Canvas;
      ForeColor = Ink;
      Font = new Font("Microsoft YaHei UI", 10F);
      AutoScaleMode = AutoScaleMode.Dpi;
      ClientSize = confirmation ? new Size(560, 378) : new Size(520, 270);

      UninstallHeaderPanel header = new UninstallHeaderPanel
      {
        Dock = DockStyle.Top,
        Height = confirmation ? 108 : 92
      };
      PictureBox mark = new PictureBox
      {
        Location = new Point(26, confirmation ? 24 : 18),
        Size = new Size(58, 58),
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.Transparent
      };
      try
      {
        Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (icon != null) mark.Image = icon.ToBitmap();
      }
      catch { }
      Label titleLabel = new Label
      {
        AutoSize = true,
        Text = title,
        Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold),
        ForeColor = Ink,
        BackColor = Color.Transparent,
        Location = new Point(101, confirmation ? 27 : 20)
      };
      Label subtitle = new Label
      {
        AutoSize = true,
        Text = confirmation ? "确认删除范围，个人皮肤数据将继续保留" : "程序没有完成请求的操作",
        Font = new Font("Microsoft YaHei UI", 9F),
        ForeColor = Muted,
        BackColor = Color.Transparent,
        Location = new Point(104, confirmation ? 67 : 58)
      };
      header.Controls.Add(mark);
      header.Controls.Add(titleLabel);
      header.Controls.Add(subtitle);
      Controls.Add(header);

      if (confirmation)
      {
        Controls.Add(CreateScopeRow("×", "将删除", "管理器程序、皮肤注入引擎和桌面/开始菜单快捷方式", Danger, 132));
        Controls.Add(CreateScopeRow("✓", "会保留", "skin 文件夹、已导入皮肤和当前活动配置", Teal, 198));
        Label note = new Label
        {
          AutoSize = false,
          Text = message,
          Location = new Point(31, 264),
          Size = new Size(498, 40),
          ForeColor = Muted,
          Font = new Font("Microsoft YaHei UI", 8.8F)
        };
        Controls.Add(note);

        UninstallButton cancelButton = new UninstallButton
        {
          Text = "取消",
          Size = new Size(96, 42),
          Location = new Point(330, 318),
          Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
          DialogResult = DialogResult.No
        };
        UninstallButton removeButton = new UninstallButton
        {
          Text = "确认卸载",
          Size = new Size(110, 42),
          Location = new Point(434, 318),
          Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
          Danger = true,
          DialogResult = DialogResult.Yes
        };
        Controls.Add(cancelButton);
        Controls.Add(removeButton);
        AcceptButton = cancelButton;
        CancelButton = cancelButton;
      }
      else
      {
        Label errorLabel = new Label
        {
          AutoSize = false,
          Text = message,
          Location = new Point(31, 118),
          Size = new Size(458, 72),
          ForeColor = Muted,
          Font = new Font("Microsoft YaHei UI", 9.5F)
        };
        UninstallButton closeButton = new UninstallButton
        {
          Text = "知道了",
          Size = new Size(104, 42),
          Location = new Point(386, 210),
          Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
          DialogResult = DialogResult.OK
        };
        Controls.Add(errorLabel);
        Controls.Add(closeButton);
        AcceptButton = closeButton;
        CancelButton = closeButton;
      }

      if (!string.IsNullOrEmpty(screenshotPath)) Shown += CaptureScreenshot;
    }

    private static Panel CreateScopeRow(string glyph, string label, string detail, Color accent, int top)
    {
      Panel row = new Panel
      {
        Location = new Point(30, top),
        Size = new Size(500, 55),
        BackColor = Surface
      };
      row.Paint += delegate(object sender, PaintEventArgs args)
      {
        args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath path = Rounded(new Rectangle(0, 0, row.Width - 1, row.Height - 1), 12))
        using (var pen = new Pen(Border)) args.Graphics.DrawPath(pen, path);
      };
      Label icon = new Label
      {
        AutoSize = false,
        Text = glyph,
        TextAlign = ContentAlignment.MiddleCenter,
        Location = new Point(13, 11),
        Size = new Size(32, 32),
        Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
        ForeColor = accent,
        BackColor = Color.FromArgb(242, 238, 234)
      };
      Label name = new Label
      {
        AutoSize = true,
        Text = label,
        Location = new Point(58, 8),
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
        ForeColor = Ink,
        BackColor = Color.Transparent
      };
      Label description = new Label
      {
        AutoSize = true,
        Text = detail,
        Location = new Point(58, 29),
        Font = new Font("Microsoft YaHei UI", 8.2F),
        ForeColor = Muted,
        BackColor = Color.Transparent
      };
      row.Controls.Add(icon);
      row.Controls.Add(name);
      row.Controls.Add(description);
      return row;
    }

    public static DialogResult Confirm(string screenshotPath)
    {
      using (var dialog = new UninstallDialog("卸载 Codex皮肤主题管理器",
        "以后重新安装时，仍可继续使用这些皮肤和设置。", true, screenshotPath))
        return dialog.ShowDialog();
    }

    public static void ShowError(string message)
    {
      using (var dialog = new UninstallDialog("卸载未完成", message, false, null)) dialog.ShowDialog();
    }

    private void CaptureScreenshot(object sender, EventArgs args)
    {
      Timer timer = new Timer { Interval = 250 };
      timer.Tick += delegate
      {
        timer.Stop();
        Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath));
        using (var bitmap = new Bitmap(Width, Height))
        {
          DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
          bitmap.Save(screenshotPath, System.Drawing.Imaging.ImageFormat.Png);
        }
        timer.Dispose();
        DialogResult = DialogResult.No;
        Close();
      };
      timer.Start();
    }

    internal static GraphicsPath Rounded(Rectangle rectangle, int radius)
    {
      int safe = Math.Max(1, Math.Min(radius, Math.Min(rectangle.Width, rectangle.Height) / 2));
      int diameter = safe * 2;
      GraphicsPath path = new GraphicsPath();
      path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
      path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
      path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
      path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
      path.CloseFigure();
      return path;
    }
  }

  internal sealed class UninstallHeaderPanel : Panel
  {
    public UninstallHeaderPanel()
    {
      SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    protected override void OnPaintBackground(PaintEventArgs args)
    {
      using (var gradient = new LinearGradientBrush(ClientRectangle,
        Color.FromArgb(255, 249, 244), Color.FromArgb(233, 245, 243), LinearGradientMode.Horizontal))
        args.Graphics.FillRectangle(gradient, ClientRectangle);
      using (var glow = new SolidBrush(Color.FromArgb(30, 206, 103, 82)))
        args.Graphics.FillEllipse(glow, Width - 210, -110, 250, 210);
    }

    protected override void OnPaint(PaintEventArgs args)
    {
      base.OnPaint(args);
      using (var pen = new Pen(Color.FromArgb(226, 213, 205))) args.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }
  }

  internal sealed class UninstallButton : Button
  {
    private bool hovered;
    private bool pressed;
    public bool Danger { get; set; }

    public UninstallButton()
    {
      SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
        ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
      FlatStyle = FlatStyle.Flat;
      FlatAppearance.BorderSize = 0;
      FlatAppearance.MouseDownBackColor = Color.Transparent;
      FlatAppearance.MouseOverBackColor = Color.Transparent;
      BackColor = Color.Transparent;
      UseVisualStyleBackColor = false;
      Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs eventArgs) { hovered = true; Invalidate(); base.OnMouseEnter(eventArgs); }
    protected override void OnMouseLeave(EventArgs eventArgs) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(eventArgs); }
    protected override void OnMouseDown(MouseEventArgs eventArgs) { pressed = true; Invalidate(); base.OnMouseDown(eventArgs); }
    protected override void OnMouseUp(MouseEventArgs eventArgs) { pressed = false; Invalidate(); base.OnMouseUp(eventArgs); }

    protected override void OnPaintBackground(PaintEventArgs args)
    {
      if (Parent == null)
      {
        args.Graphics.Clear(Color.FromArgb(247, 244, 239));
        return;
      }
      GraphicsState state = args.Graphics.Save();
      try
      {
        args.Graphics.TranslateTransform(-Left, -Top);
        using (var parentArgs = new PaintEventArgs(args.Graphics, new Rectangle(Left, Top, Width, Height)))
          InvokePaintBackground(Parent, parentArgs);
      }
      finally { args.Graphics.Restore(state); }
    }

    protected override void OnPaint(PaintEventArgs args)
    {
      OnPaintBackground(args);
      args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
      Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
      Color fill;
      Color border;
      Color text;
      if (Danger)
      {
        fill = pressed ? Color.FromArgb(158, 50, 50) : hovered ? Color.FromArgb(190, 69, 67) : Color.FromArgb(176, 61, 61);
        border = fill;
        text = Color.White;
      }
      else
      {
        fill = pressed ? Color.FromArgb(239, 228, 222) : hovered ? Color.FromArgb(250, 241, 235) : Color.FromArgb(255, 253, 249);
        border = hovered ? Color.FromArgb(211, 174, 159) : Color.FromArgb(226, 213, 205);
        text = Color.FromArgb(60, 45, 48);
      }
      using (GraphicsPath path = UninstallDialog.Rounded(bounds, 11))
      using (var brush = new SolidBrush(fill))
      using (var pen = new Pen(border))
      {
        args.Graphics.FillPath(brush, path);
        args.Graphics.DrawPath(pen, path);
      }
      TextRenderer.DrawText(args.Graphics, Text, Font, bounds, text,
        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
      if (Focused && ShowFocusCues)
      {
        Rectangle focus = Rectangle.Inflate(bounds, -4, -4);
        using (GraphicsPath path = UninstallDialog.Rounded(focus, 7))
        using (var pen = new Pen(Danger ? Color.White : Color.FromArgb(61, 132, 137))) args.Graphics.DrawPath(pen, path);
      }
    }
  }

  internal static class Program
  {
    private const string ProductKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\CodexDreamSkinManager";
    private const string ProductFolder = "codex-skin-manager";

    [STAThread]
    private static int Main(string[] args)
    {
      bool unattended = args.Length > 0 &&
        (string.Equals(args[0], "--remove", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(args[0], "--test-root", StringComparison.OrdinalIgnoreCase));
      try
      {
        if (args.Length > 0 && string.Equals(args[0], "--remove", StringComparison.OrdinalIgnoreCase))
          return Remove(args.Length > 1 ? args[1] : null, args.Length > 2 ? args[2] : null);
        if (args.Length > 0 && string.Equals(args[0], "--test-root", StringComparison.OrdinalIgnoreCase))
          return RemoveTestRoot(args.Length > 1 ? args[1] : null);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length > 0 && string.Equals(args[0], "--screenshot", StringComparison.OrdinalIgnoreCase))
        {
          if (args.Length < 2) throw new ArgumentException("--screenshot requires an output path");
          UninstallDialog.Confirm(Path.GetFullPath(args[1]));
          return 0;
        }
        string root = Path.GetFullPath(Path.GetDirectoryName(Application.ExecutablePath));
        if (!IsAllowedInstallRoot(root)) throw new InvalidOperationException("卸载目录不是有效的 Codex Dream Skin 安装目录。");
        DialogResult answer = UninstallDialog.Confirm(null);
        if (answer != DialogResult.Yes) return 0;
        string temp = Path.Combine(Path.GetTempPath(), "CodexDreamSkinUninstall-" + Guid.NewGuid().ToString("N") + ".exe");
        File.Copy(Application.ExecutablePath, temp, true);
        Process.Start(new ProcessStartInfo(temp, "--remove \"" + root + "\" \"" + temp + "\"") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
        return 0;
      }
      catch (Exception error)
      {
        if (!unattended) UninstallDialog.ShowError(error.Message);
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
}
