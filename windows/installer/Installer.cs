using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Diagnostics;
using System.Threading;

[assembly: AssemblyTitle("Codex Dream Skin Setup")]
[assembly: AssemblyDescription("Install Codex皮肤主题管理器 and bundled themes")]
[assembly: AssemblyCompany("Codex Dream Skin")]
[assembly: AssemblyProduct("Codex Dream Skin Setup")]
[assembly: AssemblyVersion("2.3.3.0")]
[assembly: AssemblyFileVersion("2.3.3.0")]

namespace CodexDreamSkinInstaller
{
  internal static class InstallerConstants
  {
    public const string ProductName = "Codex皮肤主题管理器";
    public const string Version = "2.3.3";
    public const string ManagerFileName = "Codex皮肤主题管理器.exe";
    public const string UninstallerFileName = "Codex皮肤主题管理器卸载程序.exe";
    public const string InstallFolderName = "codex-skin-manager";
    public const string PayloadResource = "CodexDreamSkin.Payload.zip";
    public const string LogoResource = "CodexDreamSkin.Logo.png";
    public static readonly string[] BundledSkinIds = new[] { "rose-garden", "coral-haze", "violet-riviera", "lilac-salon" };

    public static string DefaultInstallParent()
    {
      try
      {
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
          if (string.Equals(drive.Name, @"E:\", StringComparison.OrdinalIgnoreCase) && drive.IsReady)
            return drive.RootDirectory.FullName;
        }
      }
      catch { }
      if (Directory.Exists(@"E:\")) return @"E:\";
      return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
    }

    public static string InstallRootFromParent(string parent)
    {
      if (string.IsNullOrWhiteSpace(parent)) throw new InvalidDataException("请选择安装位置。");
      return Path.Combine(Path.GetFullPath(parent.Trim()), InstallFolderName);
    }
  }

  internal static class InstallActivityGuard
  {
    public static void EnsureCanInstall(string installRoot, string previousInstallRoot)
    {
      EnsureManagerIsClosed();
      EnsureManagedRuntimeIsStopped(installRoot, previousInstallRoot);
      EnsureCodexIsClosed();
    }

    private static void EnsureManagerIsClosed()
    {
      foreach (string processName in new[] { "Codex皮肤主题管理器", "Codex Dream Skin Manager" })
      {
        foreach (Process process in Process.GetProcessesByName(processName))
        {
          try
          {
            string path = GetProcessPath(process);
            if (!string.IsNullOrEmpty(path))
              throw new InvalidOperationException("请先关闭正在运行的 Codex皮肤主题管理器，然后再安装或更新。");
          }
          catch (InvalidOperationException) { throw; }
          catch { }
          finally { process.Dispose(); }
        }
      }
    }

    public static void EnsureManagedRuntimeIsStopped(params string[] installRoots)
    {
      HashSet<string> runtimePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach (string root in installRoots ?? new string[0])
      {
        if (string.IsNullOrWhiteSpace(root)) continue;
        try
        {
          runtimePaths.Add(Path.GetFullPath(Path.Combine(root, ".codex-dream-skin", "runtime", "node", "node.exe")));
        }
        catch { }
      }
      if (runtimePaths.Count == 0) return;

      foreach (Process process in Process.GetProcessesByName("node"))
      {
        try
        {
          string path = GetProcessPath(process);
          if (!string.IsNullOrEmpty(path) && runtimePaths.Contains(Path.GetFullPath(path)))
            throw new InvalidOperationException("检测到 Codex 皮肤注入引擎仍在运行。请先关闭 Codex，并等待皮肤引擎退出后再安装。");
        }
        catch (InvalidOperationException) { throw; }
        catch { }
        finally { process.Dispose(); }
      }
    }

    public static void EnsureCodexIsClosed()
    {
      EnsureCodexIsClosed(null);
    }

    public static void EnsureCodexIsClosed(string expectedTestPath)
    {
      foreach (Process process in Process.GetProcessesByName("ChatGPT"))
      {
        try
        {
          string path = GetProcessPath(process);
          bool matches = string.IsNullOrEmpty(expectedTestPath)
            ? IsCodexExecutablePath(path)
            : PathsEqual(path, expectedTestPath);
          if (matches)
            throw new InvalidOperationException("检测到 Codex 客户端仍在运行。请先完全退出 Codex，然后再安装或更新。");
        }
        catch (InvalidOperationException) { throw; }
        catch { }
        finally { process.Dispose(); }
      }
    }

    private static bool PathsEqual(string left, string right)
    {
      if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
      try { return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase); }
      catch { return false; }
    }

    private static string GetProcessPath(Process process)
    {
      return process.MainModule == null ? null : process.MainModule.FileName;
    }

    private static bool IsCodexExecutablePath(string path)
    {
      if (string.IsNullOrWhiteSpace(path) ||
        !string.Equals(Path.GetFileName(path), "ChatGPT.exe", StringComparison.OrdinalIgnoreCase)) return false;
      try
      {
        DirectoryInfo app = Directory.GetParent(Path.GetFullPath(path));
        DirectoryInfo package = app == null ? null : app.Parent;
        return app != null && package != null &&
          string.Equals(app.Name, "app", StringComparison.OrdinalIgnoreCase) &&
          package.Name.StartsWith("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase);
      }
      catch { return false; }
    }
  }

  internal sealed class InstallEngine
  {
    private readonly string installRoot;
    private readonly bool testMode;
    private readonly Action<string, int> report;
    public string CleanupWarning { get; private set; }

    public InstallEngine(string installRoot, bool testMode, Action<string, int> report)
    {
      this.installRoot = Path.GetFullPath(installRoot);
      this.testMode = testMode;
      this.report = report ?? delegate { };
    }

    public void Install()
    {
      report("检查安装文件", 5);
      if (!testMode) InstallActivityGuard.EnsureCanInstall(installRoot, GetPreviousInstallRoot());
      string parent = Directory.GetParent(installRoot).FullName;
      Directory.CreateDirectory(parent);

      string stage = installRoot + ".stage-" + Guid.NewGuid().ToString("N");
      string backup = installRoot + ".backup-" + Guid.NewGuid().ToString("N");
      bool movedOld = false;
      bool installedNew = false;
      try
      {
        report("解压程序文件", 18);
        Directory.CreateDirectory(stage);
        ExtractPayload(stage);
        ValidatePayload(stage);
        PreserveExistingStorage(stage);

        if (Directory.Exists(installRoot))
        {
          Directory.Move(installRoot, backup);
          movedOld = true;
        }
        Directory.Move(stage, installRoot);
        stage = null;
        installedNew = true;

        report("准备四款皮肤", 65);
        SeedBundledSkins();
        if (!testMode) RegisterUninstall();
        if (!testMode) CreateShortcuts();

        if (Directory.Exists(backup) && !TryDeleteDirectory(backup))
          CleanupWarning = "新版本已安装，但旧版本备份暂时无法删除：\r\n" + backup + "\r\n重启 Windows 后可手动删除该目录。";
        report("完成安装", 100);
      }
      catch (Exception installError)
      {
        try
        {
          if (installedNew && Directory.Exists(installRoot)) Directory.Delete(installRoot, true);
          if (movedOld && Directory.Exists(backup) && !Directory.Exists(installRoot)) Directory.Move(backup, installRoot);
        }
        catch (Exception rollbackError)
        {
          throw new InvalidOperationException("安装失败，并且自动恢复旧版本未能完成。旧版本备份保留在：\r\n" + backup,
            new AggregateException(installError, rollbackError));
        }
        throw;
      }
      finally
      {
        if (!string.IsNullOrEmpty(stage)) TryDeleteDirectory(stage);
      }
    }

    private static bool TryDeleteDirectory(string path)
    {
      for (int attempt = 0; attempt < 3; attempt++)
      {
        try
        {
          if (Directory.Exists(path)) Directory.Delete(path, true);
          return true;
        }
        catch { if (attempt < 2) Thread.Sleep(250); }
      }
      return !Directory.Exists(path);
    }

    private static Stream OpenResource(string resourceName)
    {
      Assembly assembly = Assembly.GetExecutingAssembly();
      Stream stream = assembly.GetManifestResourceStream(resourceName);
      if (stream != null) return stream;
      foreach (string name in assembly.GetManifestResourceNames())
      {
        if (name.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase))
        {
          stream = assembly.GetManifestResourceStream(name);
          if (stream != null) return stream;
        }
      }
      throw new InvalidDataException("安装器内缺少资源：" + resourceName);
    }

    private void ExtractPayload(string destination)
    {
      using (Stream input = OpenResource(InstallerConstants.PayloadResource))
      using (ZipArchive archive = new ZipArchive(input, ZipArchiveMode.Read, false, Encoding.UTF8))
      {
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
          string name = entry.FullName.Replace('\\', '/');
          if (name.StartsWith("/", StringComparison.Ordinal) || name.Contains(":") ||
              name.Split('/').Any(delegate(string part) { return part == "." || part == ".."; }))
            throw new InvalidDataException("安装包包含不安全路径。");
          string target = Path.GetFullPath(Path.Combine(destination, name));
          string prefix = destination.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
          if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("安装包路径越界。");
          if (string.IsNullOrEmpty(entry.Name))
          {
            Directory.CreateDirectory(target);
            continue;
          }
          Directory.CreateDirectory(Path.GetDirectoryName(target));
          entry.ExtractToFile(target, true);
        }
      }
    }

    private static void ValidatePayload(string root)
    {
      string manager = Path.Combine(root, InstallerConstants.ManagerFileName);
      string uninstaller = Path.Combine(root, InstallerConstants.UninstallerFileName);
      string engine = Path.Combine(root, ".codex-dream-skin");
      string[] required = new[]
      {
        manager, uninstaller,
        Path.Combine(engine, "assets", "renderer-inject.js"),
        Path.Combine(engine, "assets", "builtin", "rose-garden", "skin.json"),
        Path.Combine(engine, "assets", "builtin", "rose-garden", "dream-skin.css"),
        Path.Combine(engine, "assets", "builtin", "rose-garden", "art.png"),
        Path.Combine(engine, "bundled-skins", "coral-haze", "skin.json"),
        Path.Combine(engine, "bundled-skins", "violet-riviera", "skin.json"),
        Path.Combine(engine, "bundled-skins", "lilac-salon", "skin.json"),
        Path.Combine(engine, "scripts", "start-dream-skin.ps1"),
        Path.Combine(engine, "scripts", "restore-dream-skin.ps1"),
        Path.Combine(engine, "scripts", "theme-v2", "payload.mjs"),
        Path.Combine(engine, "scripts", "theme-v2", "runtime", "theme-runtime.js"),
        Path.Combine(engine, "runtime", "webp", "dwebp.exe"),
        Path.Combine(engine, "runtime", "node", "node.exe")
      };
      foreach (string path in required) if (!File.Exists(path)) throw new InvalidDataException("安装包缺少文件：" + Path.GetFileName(path));
    }

    private void SeedBundledSkins()
    {
      string skinsRoot = Path.Combine(installRoot, "skin");
      Directory.CreateDirectory(skinsRoot);
      string engine = Path.Combine(installRoot, ".codex-dream-skin");
      foreach (string skinId in InstallerConstants.BundledSkinIds)
      {
        string source = skinId == "rose-garden"
          ? Path.Combine(engine, "assets", "builtin", skinId)
          : Path.Combine(engine, "bundled-skins", skinId);
        string destination = Path.Combine(skinsRoot, skinId);
        if (Directory.Exists(destination)) continue;
        CopyDirectory(source, destination);
      }
      string active = Path.Combine(installRoot, "active-skin.json");
      if (!File.Exists(active)) WriteUtf8(active, "{\"schemaVersion\":1,\"skinId\":\"rose-garden\",\"starlightEnabled\":true}\r\n");
    }

    private void PreserveExistingStorage(string stage)
    {
      CopyStorage(installRoot, "skin", stage);
      if (testMode) return;
      string previous = GetPreviousInstallRoot();
      if (!string.IsNullOrEmpty(previous) && !string.Equals(previous, installRoot, StringComparison.OrdinalIgnoreCase))
        CopyStorage(previous, "skin", stage);
      string legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexDreamSkin");
      CopyStorage(legacy, "skins", stage);
    }

    private static string GetPreviousInstallRoot()
    {
      try
      {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\CodexDreamSkinManager"))
        {
          object value = key == null ? null : key.GetValue("InstallLocation");
          return value == null ? null : Path.GetFullPath(Convert.ToString(value));
        }
      }
      catch { return null; }
    }

    private static void CopyStorage(string sourceRoot, string sourceSkinFolder, string destinationRoot)
    {
      if (string.IsNullOrWhiteSpace(sourceRoot) || !Directory.Exists(sourceRoot)) return;
      string sourceSkins = Path.Combine(sourceRoot, sourceSkinFolder);
      string destinationSkins = Path.Combine(destinationRoot, "skin");
      if (Directory.Exists(sourceSkins))
      {
        Directory.CreateDirectory(destinationSkins);
        foreach (string source in Directory.GetDirectories(sourceSkins))
        {
          string skinId = Path.GetFileName(source);
          if (string.Equals(skinId, "coral-haze", StringComparison.OrdinalIgnoreCase)) continue;
          string destination = Path.Combine(destinationSkins, skinId);
          if (!Directory.Exists(destination)) CopyDirectory(source, destination);
        }
      }
      foreach (string name in new[] { "active-skin.json", "built-in-skin-preferences.json" })
      {
        string source = Path.Combine(sourceRoot, name);
        string destination = Path.Combine(destinationRoot, name);
        if (File.Exists(source) && !File.Exists(destination)) File.Copy(source, destination);
      }
    }

    private void RegisterUninstall()
    {
      string uninstaller = Path.Combine(installRoot, InstallerConstants.UninstallerFileName);
      using (RegistryKey key = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\CodexDreamSkinManager"))
      {
        key.SetValue("DisplayName", InstallerConstants.ProductName, RegistryValueKind.String);
        key.SetValue("DisplayVersion", InstallerConstants.Version, RegistryValueKind.String);
        key.SetValue("Publisher", "Codex Dream Skin", RegistryValueKind.String);
        key.SetValue("InstallLocation", installRoot, RegistryValueKind.String);
        key.SetValue("UninstallString", "\"" + uninstaller + "\" --uninstall", RegistryValueKind.String);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
      }
    }

    private void CreateShortcuts()
    {
      string manager = Path.Combine(installRoot, InstallerConstants.ManagerFileName);
      string programs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs");
      string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
      DeleteShortcut(Path.Combine(programs, "Codex Dream Skin Manager.lnk"));
      DeleteShortcut(Path.Combine(desktop, "Codex Dream Skin Manager.lnk"));
      CreateShortcut(Path.Combine(programs, "Codex皮肤主题管理器.lnk"), manager);
      CreateShortcut(Path.Combine(desktop, "Codex皮肤主题管理器.lnk"), manager);
    }

    private static void DeleteShortcut(string path)
    {
      try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void CreateShortcut(string path, string target)
    {
      Directory.CreateDirectory(Path.GetDirectoryName(path));
      Type shellType = Type.GetTypeFromProgID("WScript.Shell");
      if (shellType == null) throw new InvalidOperationException("Windows 快捷方式组件不可用。");
      dynamic shell = Activator.CreateInstance(shellType);
      dynamic shortcut = shell.CreateShortcut(path);
      shortcut.TargetPath = target;
      shortcut.WorkingDirectory = Path.GetDirectoryName(target);
      shortcut.Description = InstallerConstants.ProductName;
      shortcut.IconLocation = target + ",0";
      shortcut.Save();
    }

    private static void CopyDirectory(string source, string destination)
    {
      if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
      Directory.CreateDirectory(destination);
      foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        Directory.CreateDirectory(Path.Combine(destination, directory.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar)));
      foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        File.Copy(file, Path.Combine(destination, file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar)), true);
    }

    private static void WriteUtf8(string path, string content)
    {
      Directory.CreateDirectory(Path.GetDirectoryName(path));
      File.WriteAllText(path, content, new UTF8Encoding(false));
    }
  }

  internal sealed class InstallerForm : Form
  {
    private readonly DreamButton installButton;
    private readonly DreamProgressBar progress;
    private readonly Label status;
    private readonly TextBox installParent;
    private readonly Label storagePath;

    public InstallerForm()
    {
      Text = InstallerConstants.ProductName + " 安装程序";
      Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
      StartPosition = FormStartPosition.CenterScreen;
      MinimumSize = new Size(720, 560);
      ClientSize = new Size(720, 560);
      BackColor = DreamInstallerPalette.Canvas;
      Font = new Font("Microsoft YaHei UI", 10F);
      FormBorderStyle = FormBorderStyle.FixedSingle;
      MaximizeBox = false;
      DoubleBuffered = true;

      Panel header = new InstallerHeroPanel { Dock = DockStyle.Top, Height = 128, BackColor = Color.Transparent };
      PictureBox logo = new PictureBox { Location = new Point(28, 22), Size = new Size(68, 68), SizeMode = PictureBoxSizeMode.Zoom, Image = LoadLogo() };
      header.Controls.Add(logo);
      Label eyebrow = new Label { AutoSize = true, Text = "WINDOWS 皮肤安装向导", Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold), ForeColor = DreamInstallerPalette.CoralDark, BackColor = Color.Transparent, Location = new Point(116, 17) };
      Label title = new Label { AutoSize = true, Text = InstallerConstants.ProductName, Font = new Font("Microsoft YaHei UI", 23F, FontStyle.Bold), ForeColor = DreamInstallerPalette.Ink, BackColor = Color.Transparent, Location = new Point(116, 35) };
      Label subtitle = new Label { AutoSize = true, Text = "默认安装到 E:\\codex-skin-manager · 四款人像皮肤 · 星光动态层", Font = new Font("Microsoft YaHei UI", 10F), ForeColor = DreamInstallerPalette.Muted, BackColor = Color.Transparent, Location = new Point(120, 82) };
      DreamPill versionPill = new DreamPill { Text = "v" + InstallerConstants.Version, Location = new Point(620, 30), Size = new Size(66, 28) };
      header.Controls.Add(eyebrow);
      header.Controls.Add(title); header.Controls.Add(subtitle); Controls.Add(header);
      header.Controls.Add(versionPill);

      Label locationLabel = new Label { AutoSize = true, Text = "安装位置（自动创建 codex-skin-manager）", Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold), ForeColor = DreamInstallerPalette.Ink, Location = new Point(34, 152) };
      installParent = new TextBox { BorderStyle = BorderStyle.FixedSingle, Text = InstallerConstants.DefaultInstallParent(), Location = new Point(34, 178), Width = 528, BackColor = Color.White, ForeColor = DreamInstallerPalette.Ink };
      installParent.TextChanged += delegate { UpdateStoragePath(); };
      DreamButton browse = new DreamButton { Text = "浏览位置", Location = new Point(578, 172), Size = new Size(108, 38), BackColor = BackColor, ForeColor = DreamInstallerPalette.Ink };
      browse.Click += BrowseClicked;
      storagePath = new Label { AutoSize = false, Location = new Point(34, 213), Size = new Size(652, 24), ForeColor = DreamInstallerPalette.Muted, Font = new Font("Microsoft YaHei UI", 8.5F) };
      Controls.Add(locationLabel); Controls.Add(installParent); Controls.Add(browse); Controls.Add(storagePath);
      UpdateStoragePath();

      Label skinLabel = new Label { AutoSize = true, Text = "安装内容", Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold), ForeColor = DreamInstallerPalette.Ink, Location = new Point(34, 252) };
      Controls.Add(skinLabel);
      ListView skins = new ListView { Location = new Point(34, 282), Size = new Size(652, 118), View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.None, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(255, 253, 250), ForeColor = DreamInstallerPalette.Ink, Font = new Font("Microsoft YaHei UI", 9.5F) };
      skins.Columns.Add("皮肤", 185); skins.Columns.Add("说明", 452);
      skins.Items.Add(new ListViewItem(new[] { "玫瑰轻纱", "樱粉玫瑰、人像柔光与蝴蝶星芒" }));
      skins.Items.Add(new ListViewItem(new[] { "晨雾珊瑚", "暖色人像、柔和晨雾与珊瑚光感" }));
      skins.Items.Add(new ListViewItem(new[] { "哈基米", "紫色人像、星蝶光环与宇宙舞台" }));
      skins.Items.Add(new ListViewItem(new[] { "紫纱晴光", "紫纱人像、香槟沙发与室内晴光" }));
      Controls.Add(skins);

      Label shortcutNote = new Label { AutoSize = true, Text = "安装完成后自动创建桌面快捷方式；不会自动关闭或启动 Codex。", Location = new Point(34, 426), ForeColor = DreamInstallerPalette.Ink };
      Controls.Add(shortcutNote);
      status = new Label { AutoSize = false, Text = "准备就绪 · 点击安装开始复制文件", Location = new Point(34, 458), Size = new Size(430, 32), ForeColor = DreamInstallerPalette.Muted };
      Controls.Add(status);
      progress = new DreamProgressBar { Location = new Point(34, 505), Size = new Size(430, 18), BackColor = BackColor };
      Controls.Add(progress);
      installButton = new DreamButton { Text = "开始安装", Width = 154, Height = 46, Location = new Point(532, 490), BackColor = BackColor, ForeColor = Color.White, Primary = true };
      installButton.Click += InstallClicked;
      Controls.Add(installButton);
    }

    private void BrowseClicked(object sender, EventArgs args)
    {
      using (var dialog = new FolderBrowserDialog())
      {
        dialog.Description = "选择安装位置，安装程序会在其中创建 codex-skin-manager 文件夹";
        dialog.ShowNewFolderButton = true;
        try { dialog.SelectedPath = Path.GetFullPath(installParent.Text.Trim()); } catch { }
        if (dialog.ShowDialog(this) == DialogResult.OK) installParent.Text = dialog.SelectedPath;
      }
    }

    private void UpdateStoragePath()
    {
      try { storagePath.Text = "最终安装目录：" + InstallerConstants.InstallRootFromParent(installParent.Text); }
      catch { storagePath.Text = "最终安装目录：请选择有效目录"; }
    }

    private Image LoadLogo()
    {
      using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream(InstallerConstants.LogoResource))
      {
        if (input == null) return null;
        using (Image source = Image.FromStream(input)) return new Bitmap(source);
      }
    }

    private void InstallClicked(object sender, EventArgs args)
    {
      installButton.Enabled = false;
      try
      {
        string installRoot = InstallerConstants.InstallRootFromParent(installParent.Text);
        InstallEngine engine = new InstallEngine(installRoot, false,
          delegate(string message, int value) { status.Text = message; progress.Value = value; Application.DoEvents(); });
        engine.Install();
        string completionMessage = "安装完成。四款皮肤已准备好，打开管理器即可预览和应用。";
        MessageBoxIcon icon = MessageBoxIcon.Information;
        if (!string.IsNullOrEmpty(engine.CleanupWarning))
        {
          completionMessage += "\r\n\r\n" + engine.CleanupWarning;
          icon = MessageBoxIcon.Warning;
        }
        DreamDialog.ShowMessage(this, completionMessage, InstallerConstants.ProductName, icon,
          MessageBoxButtons.OK, MessageBoxDefaultButton.Button1);
        Close();
      }
      catch (Exception error)
      {
        status.Text = "安装失败：" + error.Message;
        DreamDialog.ShowMessage(this, error.Message, "安装失败", MessageBoxIcon.Error,
          MessageBoxButtons.OK, MessageBoxDefaultButton.Button1);
        installButton.Enabled = true;
      }
    }
  }

  internal static class DreamInstallerPalette
  {
    public static readonly Color Canvas = Color.FromArgb(248, 242, 238);
    public static readonly Color Surface = Color.FromArgb(255, 253, 250);
    public static readonly Color Ink = Color.FromArgb(58, 42, 48);
    public static readonly Color Muted = Color.FromArgb(116, 91, 98);
    public static readonly Color Border = Color.FromArgb(226, 205, 196);
    public static readonly Color Coral = Color.FromArgb(206, 103, 82);
    public static readonly Color CoralDark = Color.FromArgb(170, 72, 61);
    public static readonly Color Teal = Color.FromArgb(61, 132, 137);
    public static readonly Color Violet = Color.FromArgb(137, 95, 174);
    public static readonly Color Danger = Color.FromArgb(176, 61, 61);
  }

  internal static class DreamInstallerGeometry
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

  internal sealed class InstallerHeroPanel : Panel
  {
    public InstallerHeroPanel()
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
      using (var warm = new SolidBrush(Color.FromArgb(78, 234, 145, 119))) args.Graphics.FillEllipse(warm, Width - 350, -130, 340, 260);
      using (var cool = new SolidBrush(Color.FromArgb(46, 89, 164, 168))) args.Graphics.FillEllipse(cool, Width - 180, -85, 240, 210);
      using (var pen = new Pen(Color.FromArgb(226, 213, 205))) args.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
      DrawSparkle(args.Graphics, Width - 96, 36, 7, Color.FromArgb(170, 255, 255, 255));
      DrawSparkle(args.Graphics, Width - 162, 88, 4, Color.FromArgb(140, 255, 241, 208));
    }

    private void DrawSparkle(Graphics graphics, int x, int y, int radius, Color color)
    {
      using (var pen = new Pen(color, 1.4F))
      {
        graphics.DrawLine(pen, x - radius, y, x + radius, y);
        graphics.DrawLine(pen, x, y - radius, x, y + radius);
      }
    }
  }

  internal sealed class DreamPill : Control
  {
    public DreamPill()
    {
      SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
        ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
      BackColor = Color.Transparent;
      Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
      ForeColor = DreamInstallerPalette.CoralDark;
    }

    protected override void OnPaint(PaintEventArgs args)
    {
      args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
      Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
      using (GraphicsPath path = DreamInstallerGeometry.Rounded(bounds, Height / 2))
      using (var brush = new SolidBrush(Color.FromArgb(244, 229, 222))) args.Graphics.FillPath(brush, path);
      TextRenderer.DrawText(args.Graphics, Text, Font, bounds, ForeColor,
        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
  }

  internal sealed class DreamProgressBar : Control
  {
    private int progressValue;

    public DreamProgressBar()
    {
      SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
        ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
      BackColor = DreamInstallerPalette.Canvas;
    }

    public int Value
    {
      get { return progressValue; }
      set { progressValue = Math.Max(0, Math.Min(100, value)); Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs args)
    {
      args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
      Rectangle track = new Rectangle(0, 0, Width - 1, Height - 1);
      using (GraphicsPath path = DreamInstallerGeometry.Rounded(track, Height / 2))
      using (var brush = new SolidBrush(Color.FromArgb(238, 226, 220))) args.Graphics.FillPath(brush, path);
      int fillWidth = Math.Max(0, (int)Math.Round((Width - 1) * (progressValue / 100.0)));
      if (fillWidth > 0)
      {
        Rectangle fill = new Rectangle(0, 0, fillWidth, Height - 1);
        using (GraphicsPath path = DreamInstallerGeometry.Rounded(fill, Height / 2))
        using (var gradient = new LinearGradientBrush(fill, DreamInstallerPalette.Coral, DreamInstallerPalette.Teal, LinearGradientMode.Horizontal))
          args.Graphics.FillPath(gradient, path);
      }
    }
  }

  internal sealed class DreamButton : Button
  {
    private bool hovered;
    private bool pressed;
    private bool primary;
    private bool danger;

    public DreamButton()
    {
      SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
        ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
      FlatStyle = FlatStyle.Flat;
      FlatAppearance.BorderSize = 0;
      UseVisualStyleBackColor = false;
      BackColor = DreamInstallerPalette.Canvas;
      Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
      Cursor = Cursors.Hand;
    }

    public bool Primary { get { return primary; } set { primary = value; Invalidate(); } }
    public bool Danger { get { return danger; } set { danger = value; Invalidate(); } }

    protected override void OnMouseEnter(EventArgs eventArgs) { hovered = true; Invalidate(); base.OnMouseEnter(eventArgs); }
    protected override void OnMouseLeave(EventArgs eventArgs) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(eventArgs); }
    protected override void OnMouseDown(MouseEventArgs eventArgs) { pressed = true; Invalidate(); base.OnMouseDown(eventArgs); }
    protected override void OnMouseUp(MouseEventArgs eventArgs) { pressed = false; Invalidate(); base.OnMouseUp(eventArgs); }
    protected override void OnGotFocus(EventArgs eventArgs) { Invalidate(); base.OnGotFocus(eventArgs); }
    protected override void OnLostFocus(EventArgs eventArgs) { Invalidate(); base.OnLostFocus(eventArgs); }
    protected override void OnEnabledChanged(EventArgs eventArgs) { Invalidate(); base.OnEnabledChanged(eventArgs); }

    protected override void OnPaint(PaintEventArgs args)
    {
      args.Graphics.Clear(BackColor);
      args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
      Rectangle bounds = new Rectangle(1, 1, Width - 3, Height - 3);
      Color fill;
      Color border;
      Color text;
      if (!Enabled)
      {
        fill = Color.FromArgb(239, 236, 232);
        border = Color.FromArgb(224, 218, 213);
        text = Color.FromArgb(154, 145, 143);
      }
      else if (primary)
      {
        fill = pressed ? Color.FromArgb(164, 67, 54) : hovered ? Color.FromArgb(190, 84, 67) : DreamInstallerPalette.Coral;
        border = fill;
        text = Color.White;
      }
      else if (danger)
      {
        fill = pressed ? Color.FromArgb(244, 218, 215) : hovered ? Color.FromArgb(251, 235, 232) : Color.FromArgb(255, 253, 249);
        border = Color.FromArgb(218, 156, 149);
        text = DreamInstallerPalette.Danger;
      }
      else
      {
        fill = pressed ? Color.FromArgb(238, 228, 222) : hovered ? Color.FromArgb(248, 238, 232) : Color.FromArgb(255, 253, 249);
        border = hovered ? Color.FromArgb(211, 174, 159) : DreamInstallerPalette.Border;
        text = DreamInstallerPalette.Ink;
      }
      using (GraphicsPath path = DreamInstallerGeometry.Rounded(bounds, 12))
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
        Rectangle focusBounds = Rectangle.Inflate(bounds, -4, -4);
        using (GraphicsPath path = DreamInstallerGeometry.Rounded(focusBounds, 8))
        using (var pen = new Pen(primary ? Color.White : DreamInstallerPalette.Teal)) args.Graphics.DrawPath(pen, path);
      }
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
      StartPosition = FormStartPosition.CenterParent;
      FormBorderStyle = FormBorderStyle.FixedDialog;
      ShowInTaskbar = false;
      MaximizeBox = false;
      MinimizeBox = false;
      BackColor = DreamInstallerPalette.Canvas;
      Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
      AutoScaleMode = AutoScaleMode.Dpi;
      int bodyHeight = Math.Max(76, Math.Min(190, TextRenderer.MeasureText(message, Font, new Size(400, 0),
        TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height + 8));
      ClientSize = new Size(536, bodyHeight + 130);

      InstallerHeroPanel topGlow = new InstallerHeroPanel { Dock = DockStyle.Fill };
      Controls.Add(topGlow);
      DreamDialogGlyph glyph = new DreamDialogGlyph(icon) { Location = new Point(30, 28), Size = new Size(58, 58), BackColor = DreamInstallerPalette.Canvas };
      Label titleLabel = new Label { AutoSize = false, Text = title, Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold), ForeColor = DreamInstallerPalette.Ink, BackColor = Color.Transparent, Location = new Point(104, 30), Size = new Size(398, 32) };
      Label body = new Label { AutoSize = false, Text = message, Font = new Font("Microsoft YaHei UI", 9.4F), ForeColor = DreamInstallerPalette.Muted, BackColor = Color.Transparent, Location = new Point(106, 68), Size = new Size(398, bodyHeight) };
      topGlow.Controls.Add(glyph);
      topGlow.Controls.Add(titleLabel);
      topGlow.Controls.Add(body);
      AddButtons(topGlow, buttons, defaultButton, bodyHeight + 86);
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
      return new DreamButton
      {
        Text = text,
        DialogResult = result,
        Primary = primary,
        Danger = danger,
        Size = new Size(88, 40),
        Location = location,
        BackColor = DreamInstallerPalette.Canvas
      };
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
      Color accent = DreamInstallerPalette.Teal;
      string symbol = "✓";
      if (icon == MessageBoxIcon.Error) { accent = DreamInstallerPalette.Danger; symbol = "×"; }
      else if (icon == MessageBoxIcon.Warning) { accent = DreamInstallerPalette.Coral; symbol = "!"; }
      else if (icon == MessageBoxIcon.Question) { accent = DreamInstallerPalette.Violet; symbol = "?"; }
      using (var brush = new SolidBrush(Color.FromArgb(38, accent))) args.Graphics.FillEllipse(brush, 1, 1, Width - 3, Height - 3);
      using (var pen = new Pen(Color.FromArgb(140, accent), 1.2F)) args.Graphics.DrawEllipse(pen, 1, 1, Width - 3, Height - 3);
      using (var font = new Font("Microsoft YaHei UI", 21F, FontStyle.Bold))
      {
        TextRenderer.DrawText(args.Graphics, symbol, font, new Rectangle(0, 0, Width, Height), accent,
          TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
      }
    }
  }

  internal static class Program
  {
    [STAThread]
    private static int Main(string[] args)
    {
      try
      {
        string testParent = GetArgument(args, "--test-install-parent");
        string testInstall = testParent == null ? GetArgument(args, "--test-install-root") : InstallerConstants.InstallRootFromParent(testParent);
        if (testInstall != null)
        {
          new InstallEngine(testInstall, true, null).Install();
          return 0;
        }
        string testRuntimeRoot = GetArgument(args, "--test-check-runtime-root");
        if (testRuntimeRoot != null)
        {
          InstallActivityGuard.EnsureManagedRuntimeIsStopped(Path.GetFullPath(testRuntimeRoot));
          return 0;
        }
        if (args.Any(delegate(string value) { return string.Equals(value, "--test-check-codex", StringComparison.OrdinalIgnoreCase); }))
        {
          InstallActivityGuard.EnsureCodexIsClosed();
          return 0;
        }
        string testCodexPath = GetArgument(args, "--test-check-codex-path");
        if (testCodexPath != null)
        {
          InstallActivityGuard.EnsureCodexIsClosed(Path.GetFullPath(testCodexPath));
          return 0;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new InstallerForm());
        return 0;
      }
      catch (Exception error)
      {
        string testErrorLog = GetArgument(args, "--test-error-log");
        if (!string.IsNullOrEmpty(testErrorLog))
        {
          File.WriteAllText(Path.GetFullPath(testErrorLog), error.ToString(), new UTF8Encoding(false));
          return 1;
        }
        DreamDialog.ShowMessage(null, error.Message, "安装失败", MessageBoxIcon.Error,
          MessageBoxButtons.OK, MessageBoxDefaultButton.Button1);
        return 1;
      }
    }

    private static string GetArgument(string[] args, string name)
    {
      for (int index = 0; index + 1 < args.Length; index++) if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase)) return args[index + 1];
      return null;
    }
  }
}
