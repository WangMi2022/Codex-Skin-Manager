using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace CodexDreamSkinManager
{
  public sealed class RunResult
  {
    public int ExitCode { get; set; }
    public string Output { get; set; }
  }

  public sealed class CodexClientStatus
  {
    public bool IsRunning { get; set; }
    public int ProcessCount { get; set; }
  }

  internal static class CodexClientDetector
  {
    public static CodexClientStatus Detect()
    {
      var processIds = new HashSet<int>();
      foreach (string processName in new[] { "Codex", "ChatGPT" })
      {
        Process[] processes;
        try { processes = Process.GetProcessesByName(processName); }
        catch { continue; }
        foreach (Process process in processes)
        {
          try
          {
            string executablePath = null;
            try { executablePath = process.MainModule == null ? null : process.MainModule.FileName; } catch { }
            if (MatchesProcessIdentity(process.ProcessName, executablePath)) processIds.Add(process.Id);
          }
          finally { process.Dispose(); }
        }
      }
      return new CodexClientStatus { IsRunning = processIds.Count > 0, ProcessCount = processIds.Count };
    }

    internal static bool MatchesProcessIdentity(string processName, string executablePath)
    {
      string name = Path.GetFileNameWithoutExtension(processName ?? string.Empty);
      string normalizedPath = (executablePath ?? string.Empty).Replace('/', '\\');
      bool isCodexPackage = normalizedPath.IndexOf("\\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) >= 0 &&
        normalizedPath.IndexOf("\\app\\", StringComparison.OrdinalIgnoreCase) >= 0;
      if (string.Equals(name, "ChatGPT", StringComparison.OrdinalIgnoreCase)) return isCodexPackage;
      if (!string.Equals(name, "Codex", StringComparison.OrdinalIgnoreCase)) return false;
      return string.IsNullOrEmpty(normalizedPath) || isCodexPackage;
    }
  }

  public static class EngineLocator
  {
    public static string FindEngineRoot()
    {
      var candidates = new List<string>();
      string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
      candidates.Add(Path.Combine(baseDirectory, ".codex-dream-skin"));
      candidates.Add(baseDirectory);

      string statePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexDreamSkin", "state.json");
      if (File.Exists(statePath))
      {
        try
        {
          var serializer = new JavaScriptSerializer();
          var state = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(statePath, Encoding.UTF8));
          object injectorPath;
          if (state != null && state.TryGetValue("injectorPath", out injectorPath) && injectorPath != null)
          {
            string scripts = Path.GetDirectoryName(Convert.ToString(injectorPath));
            if (!string.IsNullOrEmpty(scripts)) candidates.Add(Path.GetDirectoryName(scripts));
          }
        }
        catch { }
      }

      foreach (string candidate in candidates)
      {
        if (IsEngineRoot(candidate)) return Path.GetFullPath(candidate);
      }
      return null;
    }

    public static bool IsEngineRoot(string path)
    {
      if (string.IsNullOrWhiteSpace(path)) return false;
      string builtInRoot = SkinStore.ResolveBuiltInSourceDirectory(path);
      return File.Exists(Path.Combine(path, "scripts", "start-dream-skin.ps1")) &&
        File.Exists(Path.Combine(path, "scripts", "restore-dream-skin.ps1")) &&
        File.Exists(Path.Combine(path, "scripts", "theme-v2", "payload.mjs")) &&
        File.Exists(Path.Combine(path, "scripts", "theme-v2", "runtime", "theme-runtime.js")) &&
        File.Exists(Path.Combine(path, "runtime", "webp", "dwebp.exe")) &&
        File.Exists(Path.Combine(path, "assets", "renderer-inject.js")) &&
        File.Exists(Path.Combine(builtInRoot, "skin.json")) &&
        File.Exists(Path.Combine(builtInRoot, "dream-skin.css")) &&
        File.Exists(Path.Combine(builtInRoot, "art.png")) &&
        SkinStore.HasRequiredBundledSkinSources(path);
    }
  }

  public sealed class EngineRunner
  {
    private readonly string engineRoot;
    private readonly string skinStateRoot;
    private readonly string powerShellPath;

    public EngineRunner(string engineRoot, string skinStateRoot)
    {
      if (!EngineLocator.IsEngineRoot(engineRoot)) throw new DirectoryNotFoundException("找不到完整的 Codex Dream Skin 引擎目录。");
      if (string.IsNullOrWhiteSpace(skinStateRoot)) throw new ArgumentNullException("skinStateRoot");
      this.engineRoot = Path.GetFullPath(engineRoot);
      this.skinStateRoot = Path.GetFullPath(skinStateRoot);
      powerShellPath = FindWorkingPowerShell();
      if (powerShellPath == null) throw new InvalidOperationException("找不到可工作的 PowerShell。请安装 PowerShell 7。");
    }

    public string PowerShellPath { get { return powerShellPath; } }

    public RunResult ApplySelectedSkin(bool codexWasRunning)
    {
      return RunScript("start-dream-skin.ps1", ApplyArguments(codexWasRunning, skinStateRoot), 120000);
    }

    public RunResult RestoreOfficialAppearance(bool codexWasRunning)
    {
      return RunScript("restore-dream-skin.ps1", RestoreArguments(codexWasRunning), 120000);
    }

    internal static string ApplyArguments(bool codexWasRunning, string stateRoot)
    {
      string restart = codexWasRunning ? "-RestartExisting " : string.Empty;
      return restart + "-SkinStateRoot " + Quote(Path.GetFullPath(stateRoot));
    }

    internal static string RestoreArguments(bool codexWasRunning)
    {
      return "-RestoreBaseTheme" + (codexWasRunning ? " -ForceRestart" : string.Empty);
    }

    private RunResult RunScript(string scriptName, string arguments, int timeoutMs)
    {
      string scriptPath = Path.Combine(engineRoot, "scripts", scriptName);
      string commandArguments = "-NoProfile -ExecutionPolicy Bypass -File " + Quote(scriptPath) + " " + arguments;
      var start = new ProcessStartInfo(powerShellPath, commandArguments);
      start.WorkingDirectory = engineRoot;
      start.UseShellExecute = false;
      start.CreateNoWindow = true;
      start.RedirectStandardOutput = true;
      start.RedirectStandardError = true;
      start.StandardOutputEncoding = Encoding.UTF8;
      start.StandardErrorEncoding = Encoding.UTF8;
      return RunProcess(start, timeoutMs);
    }

    internal static RunResult RunProcess(ProcessStartInfo start, int timeoutMs)
    {
      if (start == null) throw new ArgumentNullException("start");
      if (!start.RedirectStandardOutput || !start.RedirectStandardError)
        throw new ArgumentException("Engine runner requires redirected stdout and stderr.", "start");

      var output = new StringBuilder();
      var error = new StringBuilder();
      object syncRoot = new object();
      using (var outputClosed = new System.Threading.ManualResetEvent(false))
      using (var errorClosed = new System.Threading.ManualResetEvent(false))
      using (var process = new Process())
      {
        process.StartInfo = start;
        process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args)
        {
          if (args.Data == null) { outputClosed.Set(); return; }
          lock (syncRoot) output.AppendLine(args.Data);
        };
        process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args)
        {
          if (args.Data == null) { errorClosed.Set(); return; }
          lock (syncRoot) error.AppendLine(args.Data);
        };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(timeoutMs))
        {
          KillProcessTree(process);
          TryCancelAsyncReads(process);
          throw new TimeoutException("皮肤引擎操作超时。");
        }
        outputClosed.WaitOne(500);
        errorClosed.WaitOne(500);
        TryCancelAsyncReads(process);
        string outputText;
        string errorText;
        lock (syncRoot)
        {
          outputText = output.ToString();
          errorText = error.ToString();
        }
        return new RunResult
        {
          ExitCode = process.ExitCode,
          Output = (outputText + Environment.NewLine + errorText).Trim()
        };
      }
    }

    private static void TryCancelAsyncReads(Process process)
    {
      try { process.CancelOutputRead(); } catch { }
      try { process.CancelErrorRead(); } catch { }
    }

    private static void KillProcessTree(Process process)
    {
      try
      {
        string taskkill = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "taskkill.exe");
        if (File.Exists(taskkill))
        {
          var start = new ProcessStartInfo(taskkill, "/PID " + process.Id + " /T /F");
          start.UseShellExecute = false;
          start.CreateNoWindow = true;
          start.RedirectStandardOutput = true;
          start.RedirectStandardError = true;
          using (Process killer = Process.Start(start))
          {
            if (killer != null) killer.WaitForExit(5000);
          }
        }
      }
      catch { }
      try { if (!process.HasExited) process.Kill(); } catch { }
    }

    private static string FindWorkingPowerShell()
    {
      var candidates = new List<string>();
      AddPathCandidates(candidates, "pwsh.exe");
      AddPathCandidates(candidates, "powershell.exe");
      string legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
      if (File.Exists(legacy)) candidates.Add(legacy);
      foreach (string candidate in candidates)
      {
        if (ProbePowerShell(candidate)) return candidate;
      }
      return null;
    }

    private static void AddPathCandidates(List<string> candidates, string fileName)
    {
      string pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
      foreach (string part in pathValue.Split(Path.PathSeparator))
      {
        string directory = part.Trim().Trim('"');
        if (directory.Length == 0) continue;
        try
        {
          string candidate = Path.Combine(directory, fileName);
          if (File.Exists(candidate) && !candidates.Contains(candidate)) candidates.Add(candidate);
        }
        catch { }
      }
    }

    private static bool ProbePowerShell(string executable)
    {
      try
      {
        var start = new ProcessStartInfo(executable, "-NoProfile -Command \"Write-Output DREAM_SKIN_MANAGER_HOST_OK; exit 29\"");
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using (Process process = Process.Start(start))
        {
          string output = process.StandardOutput.ReadToEnd();
          process.StandardError.ReadToEnd();
          if (!process.WaitForExit(8000))
          {
            try { process.Kill(); } catch { }
            return false;
          }
          return process.ExitCode == 29 && output.IndexOf("DREAM_SKIN_MANAGER_HOST_OK", StringComparison.Ordinal) >= 0;
        }
      }
      catch { return false; }
    }

    internal static string Quote(string value)
    {
      return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
  }
}
