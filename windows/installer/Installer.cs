using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Diagnostics;
using System.Threading;
using System.Web.Script.Serialization;

[assembly: AssemblyTitle("Codex Dream Skin Setup")]
[assembly: AssemblyDescription("Install Codex皮肤主题管理器 and bundled themes")]
[assembly: AssemblyCompany("Codex Dream Skin")]
[assembly: AssemblyProduct("Codex Dream Skin Setup")]
[assembly: AssemblyVersion("2.5.4.0")]
[assembly: AssemblyFileVersion("2.5.4.0")]

namespace CodexDreamSkinInstaller
{
  internal static class InstallerConstants
  {
    public const string ProductName = "Codex皮肤主题管理器";
    public const string Version = "2.5.4";
    public const string ManagerFileName = "Codex皮肤主题管理器.exe";
    public const string UninstallerFileName = "Codex皮肤主题管理器卸载程序.exe";
    public const string InstallFolderName = "codex-skin-manager";
    public const string PayloadResource = "CodexDreamSkin.Payload.zip";
    public const string LogoResource = "CodexDreamSkin.Logo.png";

    public static string DefaultInstallParent()
    {
      return @"E:\";
    }

    public static string InstallRootFromParent(string parent)
    {
      if (string.IsNullOrWhiteSpace(parent)) throw new InvalidDataException("请选择安装位置。");
      return Path.Combine(Path.GetFullPath(parent.Trim()), InstallFolderName);
    }
  }

  internal sealed class BundledSkinCatalog
  {
    public int schemaVersion { get; set; }
    public BundledSkinCatalogEntry[] skins { get; set; }
  }

  internal sealed class BundledSkinCatalogEntry
  {
    public string id { get; set; }
  }

  internal sealed class BundledThemePreviewManifest
  {
    public string[] previews { get; set; }
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

    internal static void TestDirectoryMoveRetry(string root)
    {
      root = Path.GetFullPath(root);
      string source = Path.Combine(root, "source");
      string destination = Path.Combine(root, "destination");
      Directory.CreateDirectory(source);
      string lockedFile = Path.Combine(source, "locked.bin");
      File.WriteAllBytes(lockedFile, new byte[] { 1, 2, 3 });
      FileStream locked = new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.Read);
      Thread releaser = new Thread(new ThreadStart(delegate
      {
        Thread.Sleep(450);
        locked.Dispose();
      }));
      releaser.IsBackground = true;
      releaser.Start();
      Exception moveError = null;
      try { MoveDirectoryWithRetry(source, destination, "目录锁测试"); }
      catch (Exception error) { moveError = error; }
      finally
      {
        if (!releaser.Join(5000)) throw new TimeoutException("目录锁测试未能释放文件句柄。");
      }
      if (moveError != null) throw moveError;
      if (!Directory.Exists(destination)) throw new IOException("目录移动重试测试未创建目标目录。");
    }

    private static void MoveDirectoryWithRetry(string source, string destination, string operation)
    {
      Exception lastError = null;
      Stopwatch wait = Stopwatch.StartNew();
      int delay = 80;
      while (true)
      {
        try
        {
          Directory.Move(source, destination);
          return;
        }
        catch (IOException error) { lastError = error; }
        catch (UnauthorizedAccessException error) { lastError = error; }

        if (!Directory.Exists(source) || Directory.Exists(destination) || wait.ElapsedMilliseconds >= 30000) break;
        Thread.Sleep(delay);
        delay = Math.Min(500, delay + 80);
      }
      throw new IOException(operation + "失败，目录仍被其他程序暂时占用：\r\n" + source, lastError);
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
          MoveDirectoryWithRetry(installRoot, backup, "备份旧版本");
          movedOld = true;
        }
        MoveDirectoryWithRetry(stage, installRoot, "启用新版本");
        stage = null;
        installedNew = true;

        report("准备内置皮肤", 65);
        SeedBundledSkins();
        report("生成皮肤预览", 78);
        GenerateManagerPreviews();
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
          if (movedOld && Directory.Exists(backup) && !Directory.Exists(installRoot))
            MoveDirectoryWithRetry(backup, installRoot, "恢复旧版本");
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
        string prefix = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
          string name = entry.FullName.Replace('\\', '/');
          if (name.StartsWith("/", StringComparison.Ordinal) || name.Contains(":") ||
              name.Split('/').Any(delegate(string part) { return part == "." || part == ".."; }))
            throw new InvalidDataException("安装包包含不安全路径。");
          string combined = Path.Combine(destination, name.Replace('/', Path.DirectorySeparatorChar));
          int legacyLimit = string.IsNullOrEmpty(entry.Name) ? 248 : 260;
          if (combined.Length >= legacyLimit)
            throw new InvalidDataException("安装路径过深，无法安全解压全部皮肤。请选择更靠近磁盘根目录的安装位置。");
          string target = Path.GetFullPath(combined);
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
        Path.Combine(engine, "bundled-skins", "catalog.json"),
        Path.Combine(engine, "scripts", "start-dream-skin.ps1"),
        Path.Combine(engine, "scripts", "restore-dream-skin.ps1"),
        Path.Combine(engine, "scripts", "theme-v2", "payload.mjs"),
        Path.Combine(engine, "scripts", "theme-v2", "runtime", "theme-runtime.js"),
        Path.Combine(engine, "runtime", "webp", "dwebp.exe"),
        Path.Combine(engine, "runtime", "node", "node.exe")
      };
      foreach (string path in required) if (!File.Exists(path)) throw new InvalidDataException("安装包缺少文件：" + Path.GetFileName(path));
      foreach (string skinId in LoadBundledSkinIds(root))
      {
        string source = skinId == "rose-garden"
          ? Path.Combine(engine, "assets", "builtin", skinId)
          : Path.Combine(engine, "bundled-skins", skinId);
        bool hasV1 = File.Exists(Path.Combine(source, "skin.json")) &&
          File.Exists(Path.Combine(source, "dream-skin.css")) && File.Exists(Path.Combine(source, "art.png"));
        bool hasV2 = File.Exists(Path.Combine(source, "theme.json"));
        if (hasV1 == hasV2) throw new InvalidDataException("安装包内置皮肤不完整：" + skinId);
      }
    }

    private static string[] LoadBundledSkinIds(string root)
    {
      string catalogPath = Path.Combine(root, ".codex-dream-skin", "bundled-skins", "catalog.json");
      BundledSkinCatalog catalog;
      try { catalog = new JavaScriptSerializer().Deserialize<BundledSkinCatalog>(File.ReadAllText(catalogPath, Encoding.UTF8)); }
      catch (Exception error) { throw new InvalidDataException("安装包内置皮肤目录清单无效。", error); }
      if (catalog == null || catalog.schemaVersion != 1 || catalog.skins == null || catalog.skins.Length == 0)
        throw new InvalidDataException("安装包内置皮肤目录清单无效。");
      var seen = new HashSet<string>(StringComparer.Ordinal);
      var ids = new List<string>();
      foreach (BundledSkinCatalogEntry entry in catalog.skins)
      {
        string id = entry == null ? null : entry.id;
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || !IsSafeSkinId(id) || !seen.Add(id))
          throw new InvalidDataException("安装包内置皮肤目录清单包含无效或重复 ID。");
        ids.Add(id);
      }
      if (!seen.Contains("rose-garden")) throw new InvalidDataException("安装包内置皮肤目录清单缺少默认皮肤。");
      return ids.ToArray();
    }

    private static bool IsSafeSkinId(string value)
    {
      if (value == null || value.Length == 0 ||
        ((value[0] < 'a' || value[0] > 'z') && (value[0] < '0' || value[0] > '9'))) return false;
      foreach (char character in value)
      {
        if ((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9') ||
          character == '.' || character == '_' || character == '-') continue;
        return false;
      }
      return true;
    }

    private void SeedBundledSkins()
    {
      string skinsRoot = Path.Combine(installRoot, "skin");
      Directory.CreateDirectory(skinsRoot);
      string engine = Path.Combine(installRoot, ".codex-dream-skin");
      foreach (string skinId in LoadBundledSkinIds(installRoot))
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

    private void GenerateManagerPreviews()
    {
      string decoder = Path.Combine(installRoot, ".codex-dream-skin", "runtime", "webp", "dwebp.exe");
      if (!File.Exists(decoder)) throw new FileNotFoundException("安装器缺少 WebP 预览解码器。", decoder);
      string skinsRoot = Path.Combine(installRoot, "skin");
      foreach (string directory in Directory.GetDirectories(skinsRoot))
      {
        string manifestPath = Path.Combine(directory, "theme.json");
        if (!File.Exists(manifestPath)) continue;
        string output = Path.Combine(directory, ".manager-preview.png");
        if (IsValidManagerPreview(output)) continue;

        BundledThemePreviewManifest manifest;
        try
        {
          manifest = new JavaScriptSerializer().Deserialize<BundledThemePreviewManifest>(File.ReadAllText(manifestPath, Encoding.UTF8));
        }
        catch (Exception error)
        {
          throw new InvalidDataException("无法读取皮肤预览配置：" + Path.GetFileName(directory), error);
        }
        if (manifest == null || manifest.previews == null || manifest.previews.Length == 0)
          throw new InvalidDataException("皮肤缺少预览图配置：" + Path.GetFileName(directory));

        string relative = manifest.previews[0].Replace('\\', '/');
        if (!relative.StartsWith("previews/", StringComparison.Ordinal) ||
          !relative.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
          relative.Contains(":") || relative.Split('/').Any(delegate(string part) { return part == "." || part == ".."; }))
          throw new InvalidDataException("皮肤预览图路径无效：" + Path.GetFileName(directory));
        string source = Path.GetFullPath(Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(source))
          throw new InvalidDataException("皮肤预览图不存在：" + Path.GetFileName(directory));

        string temporary = output + ".tmp-" + Guid.NewGuid().ToString("N") + ".png";
        try
        {
          var start = new ProcessStartInfo(decoder,
            QuoteProcessArgument(source) + " -o " + QuoteProcessArgument(temporary));
          start.UseShellExecute = false;
          start.CreateNoWindow = true;
          start.RedirectStandardOutput = true;
          start.RedirectStandardError = true;
          using (Process process = Process.Start(start))
          {
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(20000))
            {
              try { process.Kill(); } catch { }
              throw new TimeoutException("生成皮肤预览超时：" + Path.GetFileName(directory));
            }
            if (process.ExitCode != 0 || !IsValidManagerPreview(temporary))
              throw new InvalidDataException("生成皮肤预览失败：" + Path.GetFileName(directory) + " " + (stderr + " " + stdout).Trim());
          }
          if (File.Exists(output)) File.Delete(output);
          File.Move(temporary, output);
        }
        finally
        {
          if (File.Exists(temporary)) File.Delete(temporary);
        }
      }
    }

    private static string QuoteProcessArgument(string value)
    {
      return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static bool IsValidManagerPreview(string path)
    {
      if (!File.Exists(path)) return false;
      FileInfo info = new FileInfo(path);
      if (info.Length < 8 || info.Length > 15L * 1024L * 1024L) return false;
      byte[] signature = new byte[8];
      using (FileStream input = File.OpenRead(path))
      {
        if (input.Read(signature, 0, signature.Length) != signature.Length) return false;
      }
      byte[] expected = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
      for (int index = 0; index < expected.Length; index++) if (signature[index] != expected[index]) return false;
      return true;
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
    private static readonly Color Canvas = Color.FromArgb(247, 244, 239);
    private static readonly Color Surface = Color.FromArgb(255, 253, 249);
    private static readonly Color Ink = Color.FromArgb(60, 45, 48);
    private static readonly Color Muted = Color.FromArgb(105, 87, 91);
    private static readonly Color Border = Color.FromArgb(226, 213, 205);
    private static readonly Color Coral = Color.FromArgb(206, 103, 82);
    private static readonly Color Teal = Color.FromArgb(61, 132, 137);

    private readonly InstallerButton installButton;
    private readonly InstallerButton browseButton;
    private readonly InstallerProgressBar progress;
    private readonly Label status;
    private readonly TextBox installParent;
    private readonly Label storagePath;
    private readonly string screenshotPath;
    private readonly bool verifyButtonPaint;
    private BackgroundWorker installWorker;

    public InstallerForm(string screenshotPath, bool verifyButtonPaint)
    {
      this.screenshotPath = screenshotPath;
      this.verifyButtonPaint = verifyButtonPaint;
      Text = InstallerConstants.ProductName + " 安装程序";
      try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
      StartPosition = FormStartPosition.CenterScreen;
      ClientSize = new Size(760, 590);
      BackColor = Canvas;
      ForeColor = Ink;
      Font = new Font("Microsoft YaHei UI", 10F);
      FormBorderStyle = FormBorderStyle.FixedSingle;
      MaximizeBox = false;
      AutoScaleMode = AutoScaleMode.Dpi;

      InstallerHeaderPanel header = new InstallerHeaderPanel { Dock = DockStyle.Top, Height = 124 };
      PictureBox logo = new PictureBox
      {
        Location = new Point(30, 26),
        Size = new Size(70, 70),
        SizeMode = PictureBoxSizeMode.Zoom,
        Image = LoadLogo(),
        BackColor = Color.Transparent
      };
      Label step = new Label
      {
        AutoSize = true,
        Text = "安装向导  ·  v" + InstallerConstants.Version,
        Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold),
        ForeColor = Teal,
        BackColor = Color.Transparent,
        Location = new Point(122, 22)
      };
      Label title = new Label
      {
        AutoSize = true,
        Text = InstallerConstants.ProductName,
        Font = new Font("Microsoft YaHei UI", 22F, FontStyle.Bold),
        ForeColor = Ink,
        BackColor = Color.Transparent,
        Location = new Point(118, 42)
      };
      Label subtitle = new Label
      {
        AutoSize = true,
        Text = "29 套离线皮肤 · 星光动态引擎 · schema v1 / v2",
        Font = new Font("Microsoft YaHei UI", 9.5F),
        ForeColor = Muted,
        BackColor = Color.Transparent,
        Location = new Point(122, 83)
      };
      header.Controls.Add(logo);
      header.Controls.Add(step);
      header.Controls.Add(title);
      header.Controls.Add(subtitle);
      Controls.Add(header);

      Label locationLabel = SectionLabel("安装到", new Point(30, 146));
      Controls.Add(locationLabel);
      InstallerRoundedPanel pathHost = new InstallerRoundedPanel
      {
        Location = new Point(30, 173),
        Size = new Size(574, 46),
        Radius = 11,
        BorderColor = Border,
        BackColor = Surface
      };
      installParent = new TextBox
      {
        BorderStyle = BorderStyle.None,
        Text = InstallerConstants.DefaultInstallParent(),
        Location = new Point(14, 12),
        Size = new Size(544, 25),
        BackColor = Surface,
        ForeColor = Ink,
        Font = new Font("Microsoft YaHei UI", 10.5F),
        AccessibleName = "安装位置"
      };
      installParent.TextChanged += delegate { UpdateStoragePath(); };
      pathHost.Controls.Add(installParent);
      browseButton = new InstallerButton
      {
        Text = "选择目录",
        Location = new Point(616, 172),
        Size = new Size(114, 48),
        Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
        Cursor = Cursors.Hand
      };
      browseButton.Click += BrowseClicked;
      storagePath = new Label
      {
        AutoSize = false,
        Location = new Point(32, 226),
        Size = new Size(698, 24),
        ForeColor = Muted,
        Font = new Font("Microsoft YaHei UI", 8.5F),
        AutoEllipsis = true
      };
      Controls.Add(pathHost);
      Controls.Add(browseButton);
      Controls.Add(storagePath);
      UpdateStoragePath();

      Controls.Add(SectionLabel("将安装以下内容", new Point(30, 260)));
      InstallerRoundedPanel contents = new InstallerRoundedPanel
      {
        Location = new Point(30, 288),
        Size = new Size(700, 150),
        Radius = 14,
        BorderColor = Border,
        BackColor = Surface
      };
      contents.Controls.Add(CreateFeatureRow("29", "29 套离线皮肤", "含精选原创皮肤与 awesome schema v2 主题", 0));
      contents.Controls.Add(CreateFeatureRow("✦", "动态星光特效", "顶部总开关随时开启或关闭，偏好自动保存", 50));
      contents.Controls.Add(CreateFeatureRow("✓", "管理器与快捷方式", "安装后可直接预览、切换、导入和导出皮肤", 100));
      Controls.Add(contents);

      Label safeNote = new Label
      {
        AutoSize = true,
        Text = "安装不会自动关闭或启动 Codex，现有皮肤与设置会被保留。",
        Location = new Point(31, 456),
        ForeColor = Muted,
        Font = new Font("Microsoft YaHei UI", 8.8F)
      };
      status = new Label
      {
        AutoSize = false,
        Text = "准备就绪",
        Location = new Point(31, 496),
        Size = new Size(516, 24),
        ForeColor = Ink,
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
        AutoEllipsis = true
      };
      progress = new InstallerProgressBar
      {
        Location = new Point(31, 526),
        Size = new Size(516, 10),
        Value = 0
      };
      installButton = new InstallerButton
      {
        Text = "开始安装",
        Location = new Point(574, 499),
        Size = new Size(156, 46),
        Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
        Cursor = Cursors.Hand,
        Primary = true
      };
      installButton.Click += InstallClicked;
      Controls.Add(safeNote);
      Controls.Add(status);
      Controls.Add(progress);
      Controls.Add(installButton);
      AcceptButton = installButton;

      FormClosing += delegate(object sender, FormClosingEventArgs args)
      {
        if (installWorker == null || !installWorker.IsBusy) return;
        args.Cancel = true;
        status.Text = "正在安装，请稍候完成后再关闭窗口。";
      };
      if (!string.IsNullOrEmpty(screenshotPath)) Shown += CaptureScreenshot;
    }

    private static Label SectionLabel(string text, Point location)
    {
      return new Label
      {
        AutoSize = true,
        Text = text,
        Location = location,
        ForeColor = Ink,
        Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold)
      };
    }

    private static Panel CreateFeatureRow(string marker, string title, string description, int top)
    {
      Panel row = new Panel
      {
        Location = new Point(1, top),
        Size = new Size(698, 50),
        BackColor = Color.Transparent
      };
      Label icon = new Label
      {
        AutoSize = false,
        Text = marker,
        TextAlign = ContentAlignment.MiddleCenter,
        Location = new Point(16, 9),
        Size = new Size(32, 32),
        Font = new Font(marker == "✦" ? "Segoe UI Symbol" : "Microsoft YaHei UI", 9.5F, FontStyle.Bold),
        ForeColor = marker == "✦" ? Coral : Teal,
        BackColor = Color.FromArgb(241, 238, 233)
      };
      Label name = new Label
      {
        AutoSize = true,
        Text = title,
        Location = new Point(62, 7),
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
        ForeColor = Ink,
        BackColor = Color.Transparent
      };
      Label detail = new Label
      {
        AutoSize = true,
        Text = description,
        Location = new Point(62, 27),
        Font = new Font("Microsoft YaHei UI", 8.2F),
        ForeColor = Muted,
        BackColor = Color.Transparent
      };
      if (top > 0)
      {
        Panel divider = new Panel
        {
          Location = new Point(62, 0),
          Size = new Size(618, 1),
          BackColor = Border
        };
        row.Controls.Add(divider);
      }
      row.Controls.Add(icon);
      row.Controls.Add(name);
      row.Controls.Add(detail);
      return row;
    }

    private void BrowseClicked(object sender, EventArgs args)
    {
      using (var dialog = new FolderBrowserDialog())
      {
        dialog.Description = "选择父目录，安装程序会自动创建 codex-skin-manager 文件夹";
        dialog.ShowNewFolderButton = true;
        try { dialog.SelectedPath = Path.GetFullPath(installParent.Text.Trim()); } catch { }
        if (dialog.ShowDialog(this) == DialogResult.OK) installParent.Text = dialog.SelectedPath;
      }
    }

    private void UpdateStoragePath()
    {
      try
      {
        string target = InstallerConstants.InstallRootFromParent(installParent.Text);
        string drive = Path.GetPathRoot(target);
        bool available = !string.IsNullOrEmpty(drive) && Directory.Exists(drive);
        storagePath.ForeColor = available ? Muted : Coral;
        storagePath.Text = available
          ? "目标文件夹：" + target
          : "目标文件夹：" + target + "  ·  当前磁盘不可用，可点击“选择目录”更改";
      }
      catch
      {
        storagePath.ForeColor = Coral;
        storagePath.Text = "请选择有效的安装位置。";
      }
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
      if (installWorker != null && installWorker.IsBusy) return;
      string installRoot;
      try
      {
        installRoot = InstallerConstants.InstallRootFromParent(installParent.Text);
        string drive = Path.GetPathRoot(installRoot);
        if (string.IsNullOrEmpty(drive) || !Directory.Exists(drive))
          throw new DirectoryNotFoundException("所选磁盘当前不可用，请连接磁盘或选择其他位置。");
      }
      catch (Exception error)
      {
        InstallerNoticeDialog.Show(this, "安装位置不可用", error.Message, InstallerNoticeTone.Error);
        installParent.Focus();
        return;
      }

      SetInstallControls(false);
      status.Text = "正在准备安装...";
      progress.Value = 2;
      installWorker = new BackgroundWorker { WorkerReportsProgress = true };
      installWorker.DoWork += delegate(object workerSender, DoWorkEventArgs workerArgs)
      {
        BackgroundWorker worker = (BackgroundWorker)workerSender;
        InstallEngine engine = new InstallEngine(installRoot, false,
          delegate(string message, int value) { worker.ReportProgress(value, message); });
        engine.Install();
        workerArgs.Result = engine.CleanupWarning;
      };
      installWorker.ProgressChanged += delegate(object workerSender, ProgressChangedEventArgs progressArgs)
      {
        progress.Value = Math.Max(0, Math.Min(100, progressArgs.ProgressPercentage));
        status.Text = Convert.ToString(progressArgs.UserState);
      };
      installWorker.RunWorkerCompleted += delegate(object workerSender, RunWorkerCompletedEventArgs completedArgs)
      {
        SetInstallControls(true);
        if (completedArgs.Error != null)
        {
          status.Text = "安装失败：" + completedArgs.Error.Message;
          progress.Value = 0;
          InstallerNoticeDialog.Show(this, "安装失败", completedArgs.Error.Message, InstallerNoticeTone.Error);
          installWorker.Dispose();
          installWorker = null;
          return;
        }
        progress.Value = 100;
        status.Text = "安装完成，可以开始使用皮肤管理器。";
        string warning = Convert.ToString(completedArgs.Result);
        string message = "29 套皮肤和动态星光引擎已准备好，桌面快捷方式也已创建。";
        InstallerNoticeTone tone = InstallerNoticeTone.Success;
        if (!string.IsNullOrEmpty(warning))
        {
          message += "\r\n\r\n" + warning;
          tone = InstallerNoticeTone.Warning;
        }
        DialogResult next = InstallerNoticeDialog.Confirm(this, "安装完成", message, tone,
          "打开管理器", "完成");
        installWorker.Dispose();
        installWorker = null;
        if (next == DialogResult.Yes)
        {
          try { Process.Start(Path.Combine(installRoot, InstallerConstants.ManagerFileName)); }
          catch (Exception error)
          {
            InstallerNoticeDialog.Show(this, "无法打开管理器",
              "安装已经完成，但管理器未能自动打开。\r\n\r\n" + error.Message,
              InstallerNoticeTone.Error);
          }
        }
        Close();
      };
      installWorker.RunWorkerAsync();
    }

    private void SetInstallControls(bool enabled)
    {
      installParent.Enabled = enabled;
      browseButton.Enabled = enabled;
      installButton.Enabled = enabled;
      installButton.Text = enabled ? "开始安装" : "正在安装...";
    }

    private void CaptureScreenshot(object sender, EventArgs args)
    {
      System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 250 };
      timer.Tick += delegate
      {
        timer.Stop();
        Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath));
        using (var bitmap = new Bitmap(Width, Height))
        {
          DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
          if (verifyButtonPaint)
            ButtonArtifactPixels = CountButtonEdgeArtifacts(bitmap, browseButton) +
              CountButtonEdgeArtifacts(bitmap, installButton);
          bitmap.Save(screenshotPath, System.Drawing.Imaging.ImageFormat.Png);
        }
        timer.Dispose();
        Close();
      };
      timer.Start();
    }

    public int ButtonArtifactPixels { get; private set; }

    private int CountButtonEdgeArtifacts(Bitmap bitmap, Control button)
    {
      Point screen = button.PointToScreen(Point.Empty);
      Rectangle bounds = new Rectangle(screen.X - Left, screen.Y - Top, button.Width, button.Height);
      int count = 0;
      int rightStart = Math.Max(bounds.Left, bounds.Right - 5);
      int bottomStart = Math.Max(bounds.Top, bounds.Bottom - 5);
      for (int y = bounds.Top + 8; y < bounds.Bottom - 8 && y < bitmap.Height; y++)
        for (int x = rightStart; x < bounds.Right && x < bitmap.Width; x++)
          if (IsNearBlack(bitmap.GetPixel(x, y))) count++;
      for (int y = bottomStart; y < bounds.Bottom && y < bitmap.Height; y++)
        for (int x = bounds.Left + 8; x < bounds.Right - 8 && x < bitmap.Width; x++)
          if (IsNearBlack(bitmap.GetPixel(x, y))) count++;
      return count;
    }

    private static bool IsNearBlack(Color color)
    {
      return color.A > 200 && color.R < 40 && color.G < 40 && color.B < 40;
    }
  }

  internal enum InstallerNoticeTone
  {
    Success,
    Warning,
    Error
  }

  internal sealed class InstallerNoticeDialog : Form
  {
    public InstallerNoticeDialog(string title, string message, InstallerNoticeTone tone)
      : this(title, message, tone, "知道了", null, false)
    {
    }

    private InstallerNoticeDialog(string title, string message, InstallerNoticeTone tone,
      string primaryText, string secondaryText, bool confirmation)
    {
      Text = title;
      try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
      StartPosition = FormStartPosition.CenterParent;
      FormBorderStyle = FormBorderStyle.FixedDialog;
      ShowInTaskbar = false;
      MaximizeBox = false;
      MinimizeBox = false;
      BackColor = Color.FromArgb(247, 244, 239);
      Font = new Font("Microsoft YaHei UI", 10F);
      AutoScaleMode = AutoScaleMode.Dpi;
      Size measured = TextRenderer.MeasureText(message ?? string.Empty, Font, new Size(390, 0), TextFormatFlags.WordBreak);
      int messageHeight = Math.Max(52, Math.Min(150, measured.Height + 4));
      ClientSize = new Size(510, 170 + messageHeight);

      Color accent = tone == InstallerNoticeTone.Success ? Color.FromArgb(61, 132, 137) :
        tone == InstallerNoticeTone.Warning ? Color.FromArgb(206, 103, 82) : Color.FromArgb(176, 61, 61);
      Label glyph = new Label
      {
        AutoSize = false,
        Text = tone == InstallerNoticeTone.Success ? "✓" : "!",
        TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold),
        ForeColor = accent,
        BackColor = Color.FromArgb(240, 235, 231),
        Location = new Point(26, 25),
        Size = new Size(44, 44)
      };
      Label titleLabel = new Label
      {
        AutoSize = true,
        Text = title,
        Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold),
        ForeColor = Color.FromArgb(60, 45, 48),
        Location = new Point(87, 24)
      };
      Label messageLabel = new Label
      {
        AutoSize = false,
        Text = message,
        Font = new Font("Microsoft YaHei UI", 9.5F),
        ForeColor = Color.FromArgb(105, 87, 91),
        Location = new Point(89, 60),
        Size = new Size(392, messageHeight)
      };
      InstallerButton primaryButton = new InstallerButton
      {
        Text = primaryText,
        Size = new Size(Math.Max(104, TextRenderer.MeasureText(primaryText, Font).Width + 32), 42),
        Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
        Primary = tone != InstallerNoticeTone.Error,
        DialogResult = confirmation ? DialogResult.Yes : DialogResult.OK
      };
      primaryButton.Location = new Point(ClientSize.Width - primaryButton.Width - 24, ClientSize.Height - 62);
      InstallerButton secondaryButton = null;
      if (!string.IsNullOrEmpty(secondaryText))
      {
        secondaryButton = new InstallerButton
        {
          Text = secondaryText,
          Size = new Size(Math.Max(92, TextRenderer.MeasureText(secondaryText, Font).Width + 28), 42),
          Location = new Point(primaryButton.Left - 102, ClientSize.Height - 62),
          Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
          DialogResult = DialogResult.No
        };
        secondaryButton.Left = primaryButton.Left - secondaryButton.Width - 10;
      }
      Controls.Add(glyph);
      Controls.Add(titleLabel);
      Controls.Add(messageLabel);
      if (secondaryButton != null) Controls.Add(secondaryButton);
      Controls.Add(primaryButton);
      AcceptButton = primaryButton;
      CancelButton = secondaryButton ?? primaryButton;
    }

    public static void Show(IWin32Window owner, string title, string message, InstallerNoticeTone tone)
    {
      using (var dialog = new InstallerNoticeDialog(title, message, tone)) dialog.ShowDialog(owner);
    }

    public static DialogResult Confirm(IWin32Window owner, string title, string message,
      InstallerNoticeTone tone, string primaryText, string secondaryText)
    {
      using (var dialog = new InstallerNoticeDialog(title, message, tone,
        primaryText, secondaryText, true)) return dialog.ShowDialog(owner);
    }

    public static void CaptureCompletion(string path)
    {
      using (var dialog = new InstallerNoticeDialog("安装完成",
        "29 套皮肤和动态星光引擎已准备好，桌面快捷方式也已创建。",
        InstallerNoticeTone.Success, "打开管理器", "完成", true))
      {
        dialog.Shown += delegate
        {
          System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 250 };
          timer.Tick += delegate
          {
            timer.Stop();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
            {
              dialog.DrawToBitmap(bitmap, new Rectangle(0, 0, dialog.Width, dialog.Height));
              bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
            timer.Dispose();
            dialog.DialogResult = DialogResult.No;
            dialog.Close();
          };
          timer.Start();
        };
        dialog.ShowDialog();
      }
    }
  }

  internal sealed class InstallerHeaderPanel : Panel
  {
    public InstallerHeaderPanel()
    {
      SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    protected override void OnPaintBackground(PaintEventArgs args)
    {
      using (var gradient = new LinearGradientBrush(ClientRectangle,
        Color.FromArgb(255, 249, 244), Color.FromArgb(233, 245, 243), LinearGradientMode.Horizontal))
        args.Graphics.FillRectangle(gradient, ClientRectangle);
      using (var glow = new SolidBrush(Color.FromArgb(34, 206, 103, 82)))
        args.Graphics.FillEllipse(glow, Width - 260, -130, 300, 250);
    }

    protected override void OnPaint(PaintEventArgs args)
    {
      base.OnPaint(args);
      using (var pen = new Pen(Color.FromArgb(226, 213, 205))) args.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }
  }

  internal sealed class InstallerRoundedPanel : Panel
  {
    private int radius = 12;
    private Color borderColor = Color.FromArgb(226, 213, 205);

    public InstallerRoundedPanel()
    {
      SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    public int Radius { get { return radius; } set { radius = Math.Max(1, value); UpdateShape(); Invalidate(); } }
    public Color BorderColor { get { return borderColor; } set { borderColor = value; Invalidate(); } }

    protected override void OnResize(EventArgs eventArgs) { base.OnResize(eventArgs); UpdateShape(); }

    private void UpdateShape()
    {
      if (Width < 2 || Height < 2) return;
      using (GraphicsPath path = Rounded(new Rectangle(0, 0, Width, Height), radius))
      {
        Region previous = Region;
        Region = new Region(path);
        if (previous != null) previous.Dispose();
      }
    }

    protected override void OnPaint(PaintEventArgs args)
    {
      args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
      using (GraphicsPath path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), radius))
      using (var pen = new Pen(borderColor)) args.Graphics.DrawPath(pen, path);
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

  internal sealed class InstallerButton : Button
  {
    private bool hovered;
    private bool pressed;
    public bool Primary { get; set; }

    public InstallerButton()
    {
      SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
        ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
      FlatStyle = FlatStyle.Flat;
      FlatAppearance.BorderSize = 0;
      FlatAppearance.MouseDownBackColor = Color.Transparent;
      FlatAppearance.MouseOverBackColor = Color.Transparent;
      UseVisualStyleBackColor = false;
      BackColor = Color.Transparent;
    }

    protected override void OnMouseEnter(EventArgs eventArgs) { hovered = true; Invalidate(); base.OnMouseEnter(eventArgs); }
    protected override void OnMouseLeave(EventArgs eventArgs) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(eventArgs); }
    protected override void OnMouseDown(MouseEventArgs eventArgs) { pressed = true; Invalidate(); base.OnMouseDown(eventArgs); }
    protected override void OnMouseUp(MouseEventArgs eventArgs) { pressed = false; Invalidate(); base.OnMouseUp(eventArgs); }
    protected override void OnEnabledChanged(EventArgs eventArgs) { Invalidate(); base.OnEnabledChanged(eventArgs); }

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
      if (!Enabled)
      {
        fill = Color.FromArgb(235, 232, 228);
        border = Color.FromArgb(220, 214, 209);
        text = Color.FromArgb(145, 137, 135);
      }
      else if (Primary)
      {
        fill = pressed ? Color.FromArgb(48, 113, 118) : hovered ? Color.FromArgb(72, 147, 152) : Color.FromArgb(61, 132, 137);
        border = fill;
        text = Color.White;
      }
      else
      {
        fill = pressed ? Color.FromArgb(239, 228, 222) : hovered ? Color.FromArgb(250, 241, 235) : Color.FromArgb(255, 253, 249);
        border = hovered ? Color.FromArgb(211, 174, 159) : Color.FromArgb(226, 213, 205);
        text = Color.FromArgb(60, 45, 48);
      }
      using (GraphicsPath path = InstallerRoundedPanel.Rounded(bounds, 11))
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
        using (GraphicsPath path = InstallerRoundedPanel.Rounded(focus, 7))
        using (var pen = new Pen(Primary ? Color.White : Color.FromArgb(61, 132, 137))) args.Graphics.DrawPath(pen, path);
      }
    }
  }

  internal sealed class InstallerProgressBar : Control
  {
    private int value;
    public int Value { get { return value; } set { this.value = Math.Max(0, Math.Min(100, value)); Invalidate(); } }

    public InstallerProgressBar()
    {
      SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
      AccessibleRole = AccessibleRole.ProgressBar;
    }

    protected override void OnPaint(PaintEventArgs args)
    {
      args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
      Rectangle track = new Rectangle(0, 0, Width - 1, Height - 1);
      using (GraphicsPath path = InstallerRoundedPanel.Rounded(track, Height / 2))
      using (var brush = new SolidBrush(Color.FromArgb(226, 220, 215))) args.Graphics.FillPath(brush, path);
      int fillWidth = (int)Math.Round((Width - 1) * value / 100D);
      if (fillWidth > 1)
      {
        Rectangle fill = new Rectangle(0, 0, fillWidth, Height - 1);
        using (GraphicsPath path = InstallerRoundedPanel.Rounded(fill, Height / 2))
        using (var brush = new SolidBrush(Color.FromArgb(61, 132, 137))) args.Graphics.FillPath(brush, path);
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
        string moveRetryTest = GetArgument(args, "--test-directory-move-retry");
        if (moveRetryTest != null)
        {
          InstallEngine.TestDirectoryMoveRetry(moveRetryTest);
          return 0;
        }
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
        string completionScreenshot = GetArgument(args, "--screenshot-completion");
        if (!string.IsNullOrEmpty(completionScreenshot))
        {
          InstallerNoticeDialog.CaptureCompletion(Path.GetFullPath(completionScreenshot));
          return 0;
        }
        string buttonRenderTest = GetArgument(args, "--test-button-render");
        string screenshotPath = string.IsNullOrEmpty(buttonRenderTest)
          ? GetArgument(args, "--screenshot")
          : buttonRenderTest;
        bool verifyButtonPaint = !string.IsNullOrEmpty(buttonRenderTest);
        InstallerForm installer = new InstallerForm(
          string.IsNullOrEmpty(screenshotPath) ? null : Path.GetFullPath(screenshotPath),
          verifyButtonPaint);
        string scaleText = GetArgument(args, "--scale");
        float scale;
        if (!string.IsNullOrEmpty(screenshotPath) &&
          float.TryParse(scaleText, NumberStyles.Float, CultureInfo.InvariantCulture, out scale) &&
          scale > 1F && scale <= 2F)
          installer.Scale(new SizeF(scale, scale));
        Application.Run(installer);
        return verifyButtonPaint && installer.ButtonArtifactPixels > 0 ? 1 : 0;
      }
      catch (Exception error)
      {
        string testErrorLog = GetArgument(args, "--test-error-log");
        if (!string.IsNullOrEmpty(testErrorLog))
        {
          File.WriteAllText(Path.GetFullPath(testErrorLog), error.ToString(), new UTF8Encoding(false));
          return 1;
        }
        InstallerNoticeDialog.Show(null, "安装失败", error.Message, InstallerNoticeTone.Error);
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
