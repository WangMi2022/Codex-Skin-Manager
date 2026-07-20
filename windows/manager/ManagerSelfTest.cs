using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace CodexDreamSkinManager
{
  internal static class ManagerSelfTest
  {
    public static int RunPackage(string engineRoot, string packagePath)
    {
      string temporary = Path.Combine(Path.GetTempPath(), "codex-dream-skin-package-test-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(temporary);
      try
      {
        if (!EngineLocator.IsEngineRoot(engineRoot)) throw new InvalidOperationException("Package test engine root is invalid.");
        var store = new SkinStore(engineRoot, Path.Combine(temporary, "state"));
        store.EnsureBundledSkins();
        SkinRecord imported = store.ImportSkin(packagePath, false);
        store.SetActiveSkin(imported.Manifest.id);
        imported = store.LoadSkins().Find(delegate(SkinRecord item) { return item.Manifest.id == imported.Manifest.id; });
        if (imported == null || imported.Manifest.schemaVersion != 2 || !File.Exists(imported.PreviewPath) ||
          Path.GetExtension(imported.PreviewPath).ToLowerInvariant() != ".png" || !imported.IsActive)
          throw new InvalidOperationException("Real schema v2 package import verification failed.");
        string exported = Path.Combine(temporary, "roundtrip.codexskin");
        store.ExportSkin(imported, exported);
        if (!File.Exists(exported) || new FileInfo(exported).Length == 0) throw new InvalidOperationException("Schema v2 package round-trip failed.");
        return 0;
      }
      catch (Exception error)
      {
        Console.Error.WriteLine(error.ToString());
        return 1;
      }
      finally
      {
        if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
      }
    }

    public static int Run(string engineRoot)
    {
      string temporary = Path.Combine(Path.GetTempPath(), "codex-dream-skin-manager-test-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(temporary);
      try
      {
        if (!EngineLocator.IsEngineRoot(engineRoot)) throw new InvalidOperationException("Self-test engine root is invalid.");
        string packageRoot = @"C:\Program Files\WindowsApps\OpenAI.Codex_26.715.3651.0_x64__2p2nqsd0c76g0\app";
        if (!CodexClientDetector.MatchesProcessIdentity("ChatGPT", Path.Combine(packageRoot, "ChatGPT.exe")) ||
          !CodexClientDetector.MatchesProcessIdentity("codex", Path.Combine(packageRoot, @"resources\codex.exe")) ||
          CodexClientDetector.MatchesProcessIdentity("ChatGPT", @"C:\Program Files\WindowsApps\OpenAI.ChatGPT_1.0.0.0_x64__test\app\ChatGPT.exe") ||
          CodexClientDetector.MatchesProcessIdentity("codex", @"C:\tools\codex.exe"))
          throw new InvalidOperationException("Codex client process identity detection failed.");
        string runningApply = CodexClientMessages.Apply("Test Skin", true);
        string stoppedApply = CodexClientMessages.Apply("Test Skin", false);
        string runningRestore = CodexClientMessages.Restore(true);
        string stoppedRestore = CodexClientMessages.Restore(false);
        if (!runningApply.Contains("正在运行") || !runningApply.Contains("重新启动") ||
          !stoppedApply.Contains("未检测到") || !stoppedApply.Contains("自动启动") ||
          !runningRestore.Contains("关闭并重新启动") || !stoppedRestore.Contains("保持关闭") ||
          EngineRunner.ApplyArguments(true, @"C:\Skin Store") != "-RestartExisting -SkinStateRoot \"C:\\Skin Store\"" ||
          EngineRunner.ApplyArguments(false, @"C:\Skin Store") != "-SkinStateRoot \"C:\\Skin Store\"" ||
          EngineRunner.RestoreArguments(true) != "-RestoreBaseTheme -ForceRestart" ||
          EngineRunner.RestoreArguments(false) != "-RestoreBaseTheme")
          throw new InvalidOperationException("Codex client status prompts or engine authorization arguments failed.");
        AssertEngineRunnerTimeoutIsEnforced();
        var store = new SkinStore(engineRoot, Path.Combine(temporary, "state"));
        store.EnsureBundledSkins();
        var initialSkins = store.LoadSkins();
        SkinRecord builtIn = initialSkins.Find(delegate(SkinRecord item) { return item.Manifest.id == SkinStore.BuiltInSkinId; });
        SkinRecord coral = initialSkins.Find(delegate(SkinRecord item) { return item.Manifest.id == "coral-haze"; });
        SkinRecord violet = initialSkins.Find(delegate(SkinRecord item) { return item.Manifest.id == "violet-riviera"; });
        SkinRecord lilac = initialSkins.Find(delegate(SkinRecord item) { return item.Manifest.id == "lilac-salon"; });
        if (initialSkins.Count != 4 || Path.GetFileName(store.SkinsRoot) != "skin" || store.GetActiveSkinId() != SkinStore.BuiltInSkinId ||
          builtIn == null || builtIn.Manifest.name != "玫瑰轻纱" || builtIn.Manifest.version != "1.1.0" ||
          builtIn.Manifest.signature != "Rose Veil ♡" || coral == null || coral.Manifest.name != "晨雾珊瑚" ||
          coral.Manifest.version != "1.0.0" || violet == null || violet.Manifest.name != "哈基米" ||
          violet.Manifest.version != "1.3.8" || lilac == null || lilac.Manifest.name != "紫纱晴光")
          throw new InvalidOperationException("Bundled skin bootstrap failed.");
        if (!store.GetStarlightEnabled()) throw new InvalidOperationException("Starlight effects should be enabled by default.");
        store.SetStarlightEnabled(false);
        store.SetActiveSkin(SkinStore.BuiltInSkinId);
        if (store.GetStarlightEnabled()) throw new InvalidOperationException("Starlight effects preference did not persist across active skin writes.");
        store.SetStarlightEnabled(true);

        string validPackage = Path.Combine(temporary, "manager-test.codexskin");
        CreatePackage(validPackage, engineRoot, "manager-test", false);
        SkinRecord imported = store.ImportSkin(validPackage, false);
        store.SetActiveSkin(imported.Manifest.id);
        if (store.LoadSkins().Count != 5 || store.GetActiveSkinId() != "manager-test") throw new InvalidOperationException("Skin import or activation failed.");
        imported = store.RenameSkin(imported, "Renamed Skin");
        if (imported.Manifest.name != "Renamed Skin" || store.GetActiveSkinId() != "manager-test" ||
          !ReadArchiveIndependentManifestName(Path.Combine(imported.DirectoryPath, "skin.json"), "Renamed Skin"))
          throw new InvalidOperationException("Skin rename did not persist or changed the active skin identity.");
        SkinRecord renamedBuiltIn = store.RenameSkin(builtIn, "Personal Rose");
        store.EnsureBuiltInSkin();
        renamedBuiltIn = store.LoadSkins().Find(delegate(SkinRecord item) { return item.Manifest.id == SkinStore.BuiltInSkinId; });
        if (renamedBuiltIn == null || renamedBuiltIn.Manifest.name != "Personal Rose")
          throw new InvalidOperationException("Built-in skin rename did not persist after resource refresh.");
        string builtInExport = Path.Combine(temporary, "renamed-built-in.codexskin");
        store.ExportSkin(renamedBuiltIn, builtInExport);
        if (!ReadArchiveManifestName(builtInExport, "Personal Rose"))
          throw new InvalidOperationException("Built-in skin export did not preserve its custom display name.");
        bool emptyRenameRejected = false;
        try { store.RenameSkin(imported, "   "); }
        catch (InvalidDataException) { emptyRenameRejected = true; }
        if (!emptyRenameRejected) throw new InvalidOperationException("Empty skin rename was accepted.");
        string exported = Path.Combine(temporary, "exported.codexskin");
        store.ExportSkin(imported, exported);
        if (!File.Exists(exported) || new FileInfo(exported).Length == 0 ||
          !ReadArchiveManifestName(exported, "Renamed Skin")) throw new InvalidOperationException("Skin export failed to preserve the renamed display name.");
        store.SetActiveSkin(SkinStore.BuiltInSkinId);
        imported = store.LoadSkins().Find(delegate(SkinRecord item) { return item.Manifest.id == "manager-test"; });
        store.DeleteSkin(imported);

        string v2Package = Path.Combine(temporary, "awesome-v2.codexskin");
        CreateV2Package(v2Package, engineRoot, "awesome-v2-test", false, false);
        SkinRecord v2 = store.ImportSkin(v2Package, false);
        store.SetActiveSkin(v2.Manifest.id);
        if (v2.Manifest.schemaVersion != 2 || v2.Manifest.name != "Awesome V2 Test" || v2.Manifest.author != "Tests" ||
          !File.Exists(v2.PreviewPath) || Path.GetExtension(v2.PreviewPath).ToLowerInvariant() != ".png" ||
          store.GetActiveSkinId() != "awesome-v2-test")
          throw new InvalidOperationException("Awesome schema v2 import or preview conversion failed.");
        v2 = store.RenameSkin(v2, "Renamed Awesome V2");
        if (v2.Manifest.name != "Renamed Awesome V2" || !ReadJsonFileName(Path.Combine(v2.DirectoryPath, "theme.json"), "Renamed Awesome V2"))
          throw new InvalidOperationException("Awesome schema v2 rename failed.");
        string v2Export = Path.Combine(temporary, "awesome-v2-export.codexskin");
        store.ExportSkin(v2, v2Export);
        if (!ReadArchiveV2Name(v2Export, "Renamed Awesome V2") || ArchiveContains(v2Export, ".manager-preview.png") ||
          !ArchiveContains(v2Export, "assets/wall.png"))
          throw new InvalidOperationException("Awesome schema v2 export failed.");
        store.SetActiveSkin(SkinStore.BuiltInSkinId);
        v2 = store.LoadSkins().Find(delegate(SkinRecord item) { return item.Manifest.id == "awesome-v2-test"; });
        store.DeleteSkin(v2);

        string unsafePackage = Path.Combine(temporary, "unsafe.codexskin");
        CreatePackage(unsafePackage, engineRoot, "unsafe-test", true);
        bool rejected = false;
        try { store.ImportSkin(unsafePackage, false); }
        catch (InvalidDataException) { rejected = true; }
        if (!rejected) throw new InvalidOperationException("Unsafe remote CSS was accepted.");

        string unsafeV2Package = Path.Combine(temporary, "unsafe-v2.codexskin");
        CreateV2Package(unsafeV2Package, engineRoot, "unsafe-v2-test", true, false);
        rejected = false;
        try { store.ImportSkin(unsafeV2Package, false); }
        catch (InvalidDataException) { rejected = true; }
        if (!rejected) throw new InvalidOperationException("Unsafe schema v2 CSS was accepted.");

        string unsafeChromePackage = Path.Combine(temporary, "unsafe-v2-chrome.codexskin");
        CreateV2Package(unsafeChromePackage, engineRoot, "unsafe-v2-chrome", false, true);
        rejected = false;
        try { store.ImportSkin(unsafeChromePackage, false); }
        catch (InvalidDataException) { rejected = true; }
        if (!rejected) throw new InvalidOperationException("Executable schema v2 chrome was accepted.");

        Console.WriteLine("{\"pass\":true,\"test\":\"skin-manager\"}");
        return 0;
      }
      catch (Exception error)
      {
        Console.Error.WriteLine(error.ToString());
        return 1;
      }
      finally
      {
        if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
      }
    }

    private static void AssertEngineRunnerTimeoutIsEnforced()
    {
      string command = Environment.GetEnvironmentVariable("ComSpec");
      if (string.IsNullOrEmpty(command) || !File.Exists(command)) return;
      var start = new ProcessStartInfo(command, "/c ping -n 6 127.0.0.1 >nul");
      start.UseShellExecute = false;
      start.CreateNoWindow = true;
      start.RedirectStandardOutput = true;
      start.RedirectStandardError = true;
      start.StandardOutputEncoding = Encoding.UTF8;
      start.StandardErrorEncoding = Encoding.UTF8;
      Stopwatch stopwatch = Stopwatch.StartNew();
      bool timedOut = false;
      try { EngineRunner.RunProcess(start, 500); }
      catch (TimeoutException) { timedOut = true; }
      stopwatch.Stop();
      if (!timedOut) throw new InvalidOperationException("Engine runner timeout was not enforced.");
      if (stopwatch.ElapsedMilliseconds > 8000)
        throw new InvalidOperationException("Engine runner timeout was enforced too late: " + stopwatch.ElapsedMilliseconds + "ms.");
    }

    private static void CreatePackage(string output, string engineRoot, string id, bool unsafeCss)
    {
      string builtInRoot = SkinStore.ResolveBuiltInSourceDirectory(engineRoot);
      string manifest = "{\"schemaVersion\":1,\"id\":\"" + id + "\",\"name\":\"Manager Test\",\"version\":\"1.0.0\",\"author\":\"Tests\",\"description\":\"Fixture\",\"brandName\":\"Manager Test\",\"brandSubtitle\":\"Fixture\",\"signature\":\"Test\"}";
      using (ZipArchive archive = ZipFile.Open(output, ZipArchiveMode.Create))
      {
        WriteText(archive, "skin.json", manifest);
        if (unsafeCss) WriteText(archive, "dream-skin.css", "@import url('https://example.com/theme.css');");
        else AddFile(archive, Path.Combine(builtInRoot, "dream-skin.css"), "dream-skin.css");
        AddFile(archive, Path.Combine(builtInRoot, "art.png"), "art.png");
      }
    }

    private static void CreateV2Package(string output, string engineRoot, string id, bool unsafeCss, bool unsafeChrome)
    {
      string builtInRoot = SkinStore.ResolveBuiltInSourceDirectory(engineRoot);
      string manifest = "{\"schemaVersion\":2,\"id\":\"" + id + "\",\"name\":\"Awesome V2 Test\",\"version\":\"1.0.0\",\"author\":{\"name\":\"Tests\"},\"description\":\"Fixture\",\"appearance\":\"dual\",\"css\":\"theme.css\",\"chrome\":\"chrome.html\",\"previews\":[\"previews/home.webp\"],\"assets\":{\"wall\":\"assets/wall.png\"}}";
      using (ZipArchive archive = ZipFile.Open(output, ZipArchiveMode.Create))
      {
        WriteText(archive, "theme.json", manifest);
        WriteText(archive, "theme.css", unsafeCss
          ? "@import url('https://example.com/theme.css');"
          : "html.codex-theme-studio { --awesome-v2-test: 1; }");
        WriteText(archive, "chrome.html", unsafeChrome
          ? "<img src=x onerror=alert(1)>"
          : "<div data-cts-layer=\"stage\"><i data-cts-text=\"hero-title\"></i></div>");
        AddFile(archive, Path.Combine(builtInRoot, "art.png"), "assets/wall.png");
        WriteBytes(archive, "previews/home.webp", Convert.FromBase64String("UklGRh4AAABXRUJQVlA4TBEAAAAvAAAAAAdQvMKXtv+BiOh/AAA="));
      }
    }

    private static void WriteText(ZipArchive archive, string name, string value)
    {
      ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
      using (Stream stream = entry.Open())
      using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(value);
    }

    private static void WriteBytes(ZipArchive archive, string name, byte[] value)
    {
      ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
      using (Stream stream = entry.Open()) stream.Write(value, 0, value.Length);
    }

    private static bool ReadJsonFileName(string path, string expectedName)
    {
      return File.ReadAllText(path, Encoding.UTF8).Contains("\"name\":\"" + expectedName + "\"");
    }

    private static bool ReadArchiveV2Name(string packagePath, string expectedName)
    {
      using (ZipArchive archive = ZipFile.OpenRead(packagePath))
      {
        ZipArchiveEntry entry = archive.GetEntry("theme.json");
        if (entry == null) return false;
        using (Stream stream = entry.Open())
        using (var reader = new StreamReader(stream, new UTF8Encoding(false, true)))
          return reader.ReadToEnd().Contains("\"name\":\"" + expectedName + "\"");
      }
    }

    private static bool ArchiveContains(string packagePath, string name)
    {
      using (ZipArchive archive = ZipFile.OpenRead(packagePath)) return archive.GetEntry(name) != null;
    }

    private static bool ReadArchiveIndependentManifestName(string manifestPath, string expectedName)
    {
      string text = File.ReadAllText(manifestPath, Encoding.UTF8);
      return text.Contains("\"name\":\"" + expectedName + "\"");
    }

    private static bool ReadArchiveManifestName(string packagePath, string expectedName)
    {
      using (ZipArchive archive = ZipFile.OpenRead(packagePath))
      {
        ZipArchiveEntry entry = archive.GetEntry("skin.json");
        if (entry == null) return false;
        using (Stream stream = entry.Open())
        using (var reader = new StreamReader(stream, new UTF8Encoding(false, true)))
          return reader.ReadToEnd().Contains("\"name\":\"" + expectedName + "\"");
      }
    }

    private static void AddFile(ZipArchive archive, string source, string name)
    {
      ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
      using (Stream input = File.OpenRead(source))
      using (Stream output = entry.Open()) input.CopyTo(output);
    }

  }
}
