using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

[assembly: AssemblyTitle("Codex皮肤主题管理器")]
[assembly: AssemblyDescription("Import, preview, and switch schema v1/v2 Codex skins")]
[assembly: AssemblyCompany("Codex Dream Skin")]
[assembly: AssemblyProduct("Codex皮肤主题管理器")]
[assembly: AssemblyVersion("2.5.4.0")]
[assembly: AssemblyFileVersion("2.5.4.0")]

namespace CodexDreamSkinManager
{
  internal static class Program
  {
    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    [STAThread]
    private static int Main(string[] args)
    {
      try { SetProcessDPIAware(); } catch { }

      if (args.Length > 0 && string.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
      {
        if (args.Length < 2) throw new ArgumentException("--self-test requires an engine directory");
        return ManagerSelfTest.Run(Path.GetFullPath(args[1]));
      }

      if (args.Length > 0 && string.Equals(args[0], "--self-test-package", StringComparison.OrdinalIgnoreCase))
      {
        if (args.Length < 3) throw new ArgumentException("--self-test-package requires an engine directory and package path");
        return ManagerSelfTest.RunPackage(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
      }

      string screenshotPath = null;
      if (args.Length > 0 && string.Equals(args[0], "--screenshot", StringComparison.OrdinalIgnoreCase))
      {
        if (args.Length < 2) throw new ArgumentException("--screenshot requires an output path");
        screenshotPath = Path.GetFullPath(args[1]);
      }

      Application.EnableVisualStyles();
      Application.SetCompatibleTextRenderingDefault(false);
      MainForm form = new MainForm(screenshotPath);
      if (!string.IsNullOrEmpty(screenshotPath) && args.Length >= 4)
      {
        int width;
        int height;
        if (int.TryParse(args[2], out width) && int.TryParse(args[3], out height))
          form.ClientSize = new System.Drawing.Size(Math.Max(1000, width), Math.Max(700, height));
      }
      Application.Run(form);
      return 0;
    }
  }
}
