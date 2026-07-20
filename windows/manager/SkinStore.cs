using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace CodexDreamSkinManager
{
  public sealed class SkinManifest
  {
    public int schemaVersion { get; set; }
    public string id { get; set; }
    public string name { get; set; }
    public string version { get; set; }
    public string author { get; set; }
    public string description { get; set; }
    public string brandName { get; set; }
    public string brandSubtitle { get; set; }
    public string signature { get; set; }
  }

  public sealed class ActiveSkinConfig
  {
    public int schemaVersion { get; set; }
    public string skinId { get; set; }
    public bool starlightEnabled { get; set; }
  }

  public sealed class BuiltInSkinPreferences
  {
    public int schemaVersion { get; set; }
    public string displayName { get; set; }
  }

  public sealed class SkinRecord
  {
    public SkinManifest Manifest { get; set; }
    public string DirectoryPath { get; set; }
    public string PreviewPath { get; set; }
    public bool IsBuiltIn { get; set; }
    public bool IsActive { get; set; }

    public override string ToString()
    {
      return Manifest == null ? base.ToString() : Manifest.name;
    }
  }

  public sealed class SkinStore
  {
    public const string BuiltInSkinId = "rose-garden";
    public static readonly string[] BundledSkinIds = new string[] { "rose-garden", "coral-haze", "violet-riviera", "lilac-salon" };
    private const int MaxCssBytes = 2 * 1024 * 1024;
    private const int MaxArtBytes = 20 * 1024 * 1024;
    private const int MaxPreviewBytes = 20 * 1024 * 1024;
    private const int MaxChromeBytes = 512 * 1024;
    private const long MaxPackageBytes = 50L * 1024 * 1024;
    private const long MaxV2AssetBytes = 24L * 1024 * 1024;
    private const long MaxV2TotalAssetBytes = 96L * 1024 * 1024;
    private const int MaxArchiveEntries = 500;
    private static readonly Regex SkinIdPattern = new Regex("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant);
    private static readonly Regex ThemeV2NamePattern = new Regex("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant);
    private static readonly Regex UnsafeCssPattern = new Regex("@import\\b|url\\s*\\(\\s*(['\"])?\\s*(?:https?:|javascript:|data:text/html)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex UnsafeChromePattern = new Regex("<\\s*(?:script|iframe|object|embed|link|meta|base)\\b|\\son[a-z0-9_-]+\\s*=|(?:javascript|vbscript)\\s*:|(?:src|href)\\s*=\\s*(['\"])?\\s*(?:https?:|data:text/html)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly byte[] PngSignature = new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a };

    private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
    private readonly string engineRoot;
    private readonly string stateRoot;
    private readonly string skinsRoot;
    private readonly string activeConfigPath;
    private readonly string builtInPreferencesPath;

    public SkinStore(string engineRoot, string stateRoot)
    {
      if (string.IsNullOrWhiteSpace(engineRoot)) throw new ArgumentNullException("engineRoot");
      this.engineRoot = Path.GetFullPath(engineRoot);
      this.stateRoot = Path.GetFullPath(stateRoot);
      skinsRoot = Path.Combine(this.stateRoot, "skin");
      activeConfigPath = Path.Combine(this.stateRoot, "active-skin.json");
      builtInPreferencesPath = Path.Combine(this.stateRoot, "built-in-skin-preferences.json");
      Directory.CreateDirectory(this.stateRoot);
      Directory.CreateDirectory(skinsRoot);
    }

    public string StateRoot { get { return stateRoot; } }
    public string SkinsRoot { get { return skinsRoot; } }
    public string ActiveConfigPath { get { return activeConfigPath; } }

    internal static string ResolveBuiltInSourceDirectory(string root)
    {
      string development = Path.Combine(root, "skins", BuiltInSkinId);
      if (Directory.Exists(development)) return development;
      development = Path.Combine(root, "manager", "builtin", BuiltInSkinId);
      if (Directory.Exists(development)) return development;
      return Path.Combine(root, "assets", "builtin", BuiltInSkinId);
    }

    internal static string ResolveBundledSourceDirectory(string root, string skinId)
    {
      string development = Path.Combine(root, "skins", skinId);
      if (Directory.Exists(development)) return development;
      if (string.Equals(skinId, BuiltInSkinId, StringComparison.Ordinal)) return ResolveBuiltInSourceDirectory(root);
      return Path.Combine(root, "bundled-skins", skinId);
    }

    internal static bool HasRequiredBundledSkinSources(string root)
    {
      foreach (string skinId in BundledSkinIds)
      {
        string source = ResolveBundledSourceDirectory(root, skinId);
        if (!File.Exists(Path.Combine(source, "skin.json")) ||
          !File.Exists(Path.Combine(source, "dream-skin.css")) ||
          !File.Exists(Path.Combine(source, "art.png"))) return false;
      }
      return true;
    }

    public void EnsureBuiltInSkin()
    {
      string source = ResolveBuiltInSourceDirectory(engineRoot);
      string destination = GetSkinDirectory(BuiltInSkinId);
      Directory.CreateDirectory(destination);
      CopyFile(Path.Combine(source, "skin.json"), Path.Combine(destination, "skin.json"));
      CopyFile(Path.Combine(source, "dream-skin.css"), Path.Combine(destination, "dream-skin.css"));
      CopyFile(Path.Combine(source, "art.png"), Path.Combine(destination, "art.png"));
      CopyFile(Path.Combine(source, "art.png"), Path.Combine(destination, "preview.png"));
      ValidateInstalledSkin(destination, BuiltInSkinId);
    }

    public void EnsureBundledSkins()
    {
      EnsureBuiltInSkin();
      foreach (string skinId in BundledSkinIds)
      {
        if (string.Equals(skinId, BuiltInSkinId, StringComparison.Ordinal)) continue;
        string destination = GetSkinDirectory(skinId);
        if (Directory.Exists(destination))
        {
          ValidateInstalledSkin(destination, skinId);
          continue;
        }

        string source = ResolveBundledSourceDirectory(engineRoot, skinId);
        string stage = destination + ".seed-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(stage);
        try
        {
          CopyFile(Path.Combine(source, "skin.json"), Path.Combine(stage, "skin.json"));
          CopyFile(Path.Combine(source, "dream-skin.css"), Path.Combine(stage, "dream-skin.css"));
          CopyFile(Path.Combine(source, "art.png"), Path.Combine(stage, "art.png"));
          string preview = Path.Combine(source, "preview.png");
          CopyFile(File.Exists(preview) ? preview : Path.Combine(source, "art.png"), Path.Combine(stage, "preview.png"));
          ValidateInstalledSkin(stage, skinId);
          try { Directory.Move(stage, destination); }
          catch (IOException)
          {
            if (!Directory.Exists(destination)) throw;
          }
        }
        finally
        {
          if (Directory.Exists(stage)) Directory.Delete(stage, true);
        }
        ValidateInstalledSkin(destination, skinId);
      }
    }

    public List<SkinRecord> LoadSkins()
    {
      EnsureBundledSkins();
      string activeId = GetActiveSkinId();
      string builtInDisplayName = GetBuiltInDisplayName();
      var records = new List<SkinRecord>();
      foreach (string directory in Directory.GetDirectories(skinsRoot))
      {
        try
        {
          string id = Path.GetFileName(directory);
          SkinManifest manifest = ValidateInstalledSkin(directory, id);
          bool isBuiltIn = string.Equals(id, BuiltInSkinId, StringComparison.Ordinal);
          if (isBuiltIn && !string.IsNullOrEmpty(builtInDisplayName)) manifest.name = builtInDisplayName;
          string preview = FindInstalledPreview(directory, manifest);
          records.Add(new SkinRecord
          {
            Manifest = manifest,
            DirectoryPath = directory,
            PreviewPath = preview,
            IsBuiltIn = isBuiltIn,
            IsActive = string.Equals(id, activeId, StringComparison.Ordinal)
          });
        }
        catch
        {
          // Invalid folders remain on disk for inspection but do not enter the UI.
        }
      }
      if (!records.Any(delegate(SkinRecord item) { return item.IsActive; }))
      {
        SetActiveSkin(BuiltInSkinId);
        foreach (SkinRecord item in records) item.IsActive = item.Manifest.id == BuiltInSkinId;
      }
      return records.OrderByDescending(delegate(SkinRecord item) { return item.IsActive; })
        .ThenByDescending(delegate(SkinRecord item) { return item.IsBuiltIn; })
        .ThenBy(delegate(SkinRecord item) { return item.Manifest.name; }, StringComparer.CurrentCultureIgnoreCase)
        .ToList();
    }

    public string GetActiveSkinId()
    {
      if (!File.Exists(activeConfigPath)) return BuiltInSkinId;
      try
      {
        ActiveSkinConfig config = serializer.Deserialize<ActiveSkinConfig>(ReadUtf8(activeConfigPath, 64 * 1024));
        if (config != null && config.schemaVersion == 1 && IsValidSkinId(config.skinId)) return config.skinId;
      }
      catch { }
      return BuiltInSkinId;
    }

    public void SetActiveSkin(string skinId)
    {
      if (!IsValidSkinId(skinId)) throw new InvalidDataException("皮肤 ID 无效。");
      ValidateInstalledSkin(GetSkinDirectory(skinId), skinId);
      var config = new ActiveSkinConfig { schemaVersion = 1, skinId = skinId, starlightEnabled = GetStarlightEnabled() };
      WriteUtf8Atomically(activeConfigPath, serializer.Serialize(config));
    }

    public bool GetStarlightEnabled()
    {
      if (!File.Exists(activeConfigPath)) return true;
      try
      {
        Dictionary<string, object> config = ParseJsonObject(ReadUtf8(activeConfigPath, 64 * 1024), "active-skin.json");
        object value;
        if (config.TryGetValue("starlightEnabled", out value))
        {
          if (value is bool) return (bool)value;
          bool parsed;
          if (bool.TryParse(Convert.ToString(value), out parsed)) return parsed;
        }
      }
      catch { }
      return true;
    }

    public void SetStarlightEnabled(bool enabled)
    {
      string activeSkinId = GetActiveSkinId();
      ValidateInstalledSkin(GetSkinDirectory(activeSkinId), activeSkinId);
      var config = new ActiveSkinConfig { schemaVersion = 1, skinId = activeSkinId, starlightEnabled = enabled };
      WriteUtf8Atomically(activeConfigPath, serializer.Serialize(config));
    }

    public SkinRecord ImportSkin(string packagePath, bool overwrite)
    {
      if (!File.Exists(packagePath)) throw new FileNotFoundException("找不到皮肤包。", packagePath);
      if (new FileInfo(packagePath).Length <= 0 || new FileInfo(packagePath).Length > MaxPackageBytes)
        throw new InvalidDataException("皮肤包必须小于 50 MB。");
      string stage = Path.Combine(stateRoot, ".skin-import-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(stage);
      try
      {
        SkinManifest manifest;
        using (ZipArchive archive = ZipFile.OpenRead(packagePath))
        {
          ValidateArchiveNames(archive);
          bool themeV2;
          ZipArchiveEntry manifestEntry = FindSingleManifest(archive, out themeV2);
          string manifestName = themeV2 ? "theme.json" : "skin.json";
          string prefix = manifestEntry.FullName.Substring(0, manifestEntry.FullName.Length - manifestName.Length);
          if (themeV2)
          {
            ExtractThemeV2Package(archive, prefix, stage);
            manifest = ValidateInstalledSkin(stage, null);
            CreateThemeV2ManagerPreview(stage, manifest);
          }
          else
          {
            byte[] manifestBytes = ReadEntry(manifestEntry, 64 * 1024);
            manifest = ParseManifest(DecodeUtf8(manifestBytes), null);
            WriteBytes(Path.Combine(stage, "skin.json"), manifestBytes);

            ZipArchiveEntry cssEntry = FindEntry(archive, prefix + "dream-skin.css", true);
            byte[] cssBytes = ReadEntry(cssEntry, MaxCssBytes);
            ValidateCss(DecodeUtf8(cssBytes));
            WriteBytes(Path.Combine(stage, "dream-skin.css"), cssBytes);

            ZipArchiveEntry artEntry = FindEntry(archive, prefix + "art.png", true);
            byte[] artBytes = ReadEntry(artEntry, MaxArtBytes);
            ValidatePng(artBytes, "art.png");
            WriteBytes(Path.Combine(stage, "art.png"), artBytes);

            ZipArchiveEntry previewEntry = FindEntry(archive, prefix + "preview.png", false);
            byte[] previewBytes = previewEntry == null ? artBytes : ReadEntry(previewEntry, MaxPreviewBytes);
            ValidatePng(previewBytes, "preview.png");
            WriteBytes(Path.Combine(stage, "preview.png"), previewBytes);
          }
        }

        ValidateInstalledSkin(stage, manifest.id);
        string destination = GetSkinDirectory(manifest.id);
        string backup = destination + ".replace-" + Guid.NewGuid().ToString("N");
        if (Directory.Exists(destination) && !overwrite) throw new IOException("已存在同名皮肤：" + manifest.name);
        try
        {
          if (Directory.Exists(destination)) Directory.Move(destination, backup);
          Directory.Move(stage, destination);
          if (Directory.Exists(backup)) Directory.Delete(backup, true);
        }
        catch
        {
          if (!Directory.Exists(destination) && Directory.Exists(backup)) Directory.Move(backup, destination);
          throw;
        }
        return LoadSkins().First(delegate(SkinRecord item) { return item.Manifest.id == manifest.id; });
      }
      finally
      {
        if (Directory.Exists(stage)) Directory.Delete(stage, true);
      }
    }

    public void ExportSkin(SkinRecord skin, string outputPath)
    {
      if (skin == null) throw new ArgumentNullException("skin");
      string fullOutput = Path.GetFullPath(outputPath);
      if (File.Exists(fullOutput)) File.Delete(fullOutput);
      using (ZipArchive archive = ZipFile.Open(fullOutput, ZipArchiveMode.Create))
      {
        if (skin.Manifest.schemaVersion == 2) AddDirectoryFiles(archive, skin.DirectoryPath);
        else
        {
          AddText(archive, serializer.Serialize(skin.Manifest), "skin.json");
          AddFile(archive, Path.Combine(skin.DirectoryPath, "dream-skin.css"), "dream-skin.css");
          AddFile(archive, Path.Combine(skin.DirectoryPath, "art.png"), "art.png");
          string preview = Path.Combine(skin.DirectoryPath, "preview.png");
          if (File.Exists(preview)) AddFile(archive, preview, "preview.png");
        }
      }
    }

    public SkinRecord RenameSkin(SkinRecord skin, string newName)
    {
      if (skin == null) throw new ArgumentNullException("skin");
      if (string.IsNullOrWhiteSpace(newName)) throw new InvalidDataException("皮肤名称不能为空。");

      string expected = GetSkinDirectory(skin.Manifest.id);
      if (!PathsEqual(expected, skin.DirectoryPath)) throw new InvalidOperationException("皮肤目录不在托管范围内。");
      SkinManifest manifest = ValidateInstalledSkin(expected, skin.Manifest.id);
      string cleanName = CleanText(newName, "", 60);
      if (string.IsNullOrWhiteSpace(cleanName)) throw new InvalidDataException("皮肤名称不能为空。");
      if (string.Equals(manifest.id, BuiltInSkinId, StringComparison.Ordinal))
      {
        var preferences = new BuiltInSkinPreferences { schemaVersion = 1, displayName = cleanName };
        WriteUtf8Atomically(builtInPreferencesPath, serializer.Serialize(preferences));
      }
      else if (!string.Equals(manifest.name, cleanName, StringComparison.Ordinal))
      {
        manifest.name = cleanName;
        if (manifest.schemaVersion == 2)
        {
          string themePath = Path.Combine(expected, "theme.json");
          Dictionary<string, object> raw = ParseJsonObject(ReadUtf8(themePath, 256 * 1024), "theme.json");
          raw["name"] = cleanName;
          WriteUtf8Atomically(themePath, serializer.Serialize(raw));
        }
        else WriteUtf8Atomically(Path.Combine(expected, "skin.json"), serializer.Serialize(manifest));
      }
      return LoadSkins().First(delegate(SkinRecord item) { return item.Manifest.id == manifest.id; });
    }

    private string GetBuiltInDisplayName()
    {
      if (!File.Exists(builtInPreferencesPath)) return null;
      try
      {
        BuiltInSkinPreferences preferences = serializer.Deserialize<BuiltInSkinPreferences>(ReadUtf8(builtInPreferencesPath, 64 * 1024));
        if (preferences == null || preferences.schemaVersion != 1 || string.IsNullOrWhiteSpace(preferences.displayName)) return null;
        string cleanName = CleanText(preferences.displayName, "", 60);
        return string.IsNullOrWhiteSpace(cleanName) ? null : cleanName;
      }
      catch { return null; }
    }

    public void DeleteSkin(SkinRecord skin)
    {
      if (skin == null) throw new ArgumentNullException("skin");
      if (skin.IsBuiltIn) throw new InvalidOperationException("内置皮肤不能删除。");
      if (skin.IsActive) throw new InvalidOperationException("请先切换到其他皮肤，再删除当前皮肤。");
      string expected = GetSkinDirectory(skin.Manifest.id);
      if (!PathsEqual(expected, skin.DirectoryPath)) throw new InvalidOperationException("皮肤目录不在托管范围内。");
      Directory.Delete(expected, true);
    }

    private SkinManifest ValidateInstalledSkin(string directory, string expectedId)
    {
      if ((expectedId != null && !IsValidSkinId(expectedId)) || !Directory.Exists(directory)) throw new InvalidDataException("皮肤目录无效。");
      bool hasV1 = File.Exists(Path.Combine(directory, "skin.json"));
      bool hasV2 = File.Exists(Path.Combine(directory, "theme.json"));
      if (hasV1 == hasV2) throw new InvalidDataException("皮肤目录必须且只能包含一种清单格式。");
      if (hasV2) return ValidateInstalledThemeV2(directory, expectedId);
      SkinManifest manifest = ParseManifest(ReadUtf8(Path.Combine(directory, "skin.json"), 64 * 1024), expectedId);
      ValidateCss(ReadUtf8(Path.Combine(directory, "dream-skin.css"), MaxCssBytes));
      ValidatePng(File.ReadAllBytes(Path.Combine(directory, "art.png")), "art.png");
      return manifest;
    }

    private SkinManifest ParseManifest(string json, string expectedId)
    {
      SkinManifest manifest;
      try { manifest = serializer.Deserialize<SkinManifest>(json); }
      catch (Exception error) { throw new InvalidDataException("skin.json 不是有效 JSON。", error); }
      if (manifest == null || manifest.schemaVersion != 1 || !IsValidSkinId(manifest.id)) throw new InvalidDataException("skin.json 的版本或 ID 无效。");
      if (expectedId != null && !string.Equals(manifest.id, expectedId, StringComparison.Ordinal)) throw new InvalidDataException("皮肤 ID 与目录不一致。");
      manifest.name = CleanText(manifest.name, manifest.id, 60);
      manifest.version = CleanText(manifest.version, "1.0.0", 24);
      manifest.author = CleanText(manifest.author, "Local skin", 60);
      manifest.description = CleanText(manifest.description, "", 160);
      manifest.brandName = CleanText(manifest.brandName, manifest.name, 80);
      manifest.brandSubtitle = CleanText(manifest.brandSubtitle, "Codex Dream Skin", 100);
      manifest.signature = CleanText(manifest.signature, manifest.name, 60);
      return manifest;
    }

    private SkinManifest ValidateInstalledThemeV2(string directory, string expectedId)
    {
      string manifestPath = Path.Combine(directory, "theme.json");
      Dictionary<string, object> raw = ParseJsonObject(ReadUtf8(manifestPath, 256 * 1024), "theme.json");
      object schemaValue;
      int schemaVersion;
      if (!raw.TryGetValue("schemaVersion", out schemaValue) || !int.TryParse(Convert.ToString(schemaValue), out schemaVersion) || schemaVersion != 2)
        throw new InvalidDataException("theme.json schemaVersion 必须为 2。");
      string id = GetText(raw, "id", "", 64);
      if (!ThemeV2NamePattern.IsMatch(id)) throw new InvalidDataException("theme.json 的 ID 无效。");
      if (expectedId != null && !string.Equals(id, expectedId, StringComparison.Ordinal)) throw new InvalidDataException("皮肤 ID 与目录不一致。");

      string cssRelative = GetText(raw, "css", "theme.css", 160);
      ValidateCss(ReadUtf8(ResolveThemeV2File(directory, cssRelative, "theme.css"), MaxCssBytes));
      object chromeValue;
      if (raw.TryGetValue("chrome", out chromeValue) && chromeValue != null && !string.IsNullOrWhiteSpace(Convert.ToString(chromeValue)))
        ValidateChrome(ReadUtf8(ResolveThemeV2File(directory, Convert.ToString(chromeValue), "chrome.html"), MaxChromeBytes));

      long totalAssetBytes = 0;
      ValidateThemeV2Assets(directory, raw, "assets", false, ref totalAssetBytes);
      ValidateThemeV2Assets(directory, raw, "motionAssets", true, ref totalAssetBytes);
      string previewRelative = GetThemeV2Preview(raw);
      if (previewRelative != null)
      {
        string previewPath = ResolveThemeV2File(directory, previewRelative, "preview");
        var previewInfo = new FileInfo(previewPath);
        if (!previewInfo.Exists || previewInfo.Length <= 0 || previewInfo.Length > MaxPreviewBytes)
          throw new InvalidDataException("v2 皮肤预览文件不存在或过大。");
      }

      object authorValue;
      string author = "Awesome Codex Skins";
      if (raw.TryGetValue("author", out authorValue))
      {
        var authorObject = authorValue as Dictionary<string, object>;
        author = authorObject == null ? CleanText(Convert.ToString(authorValue), author, 60) : GetText(authorObject, "name", author, 60);
      }
      string name = GetText(raw, "name", id, 80);
      return new SkinManifest
      {
        schemaVersion = 2,
        id = id,
        name = name,
        version = GetText(raw, "version", "1.0.0", 24),
        author = author,
        description = GetText(raw, "description", "", 240),
        brandName = name,
        brandSubtitle = "Awesome Codex Skins · schema v2",
        signature = name
      };
    }

    private static Dictionary<string, object> ParseJsonObject(string json, string name)
    {
      try
      {
        object value = new JavaScriptSerializer().DeserializeObject(json);
        Dictionary<string, object> result = value as Dictionary<string, object>;
        if (result == null) throw new InvalidDataException(name + " 必须是 JSON 对象。");
        return result;
      }
      catch (InvalidDataException) { throw; }
      catch (Exception error) { throw new InvalidDataException(name + " 不是有效 JSON。", error); }
    }

    private static string GetText(Dictionary<string, object> value, string key, string fallback, int maximumLength)
    {
      object raw;
      return value != null && value.TryGetValue(key, out raw) && raw != null
        ? CleanText(Convert.ToString(raw), fallback, maximumLength)
        : fallback;
    }

    private static string ResolveThemeV2File(string directory, string relative, string label)
    {
      if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.IndexOf(':') >= 0)
        throw new InvalidDataException(label + " 必须位于皮肤目录中。");
      string full = Path.GetFullPath(Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar)));
      string prefix = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
      if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(label + " 路径越界。");
      return full;
    }

    private static void ValidateThemeV2Assets(string directory, Dictionary<string, object> raw, string property, bool motion, ref long totalBytes)
    {
      object value;
      if (!raw.TryGetValue(property, out value) || value == null) return;
      Dictionary<string, object> assets = value as Dictionary<string, object>;
      if (assets == null) throw new InvalidDataException("theme.json 的 " + property + " 必须是对象。");
      foreach (KeyValuePair<string, object> item in assets)
      {
        if (!ThemeV2NamePattern.IsMatch(item.Key)) throw new InvalidDataException("v2 素材键无效：" + item.Key);
        string relative = Convert.ToString(item.Value);
        string file = ResolveThemeV2File(directory, relative, property + "." + item.Key);
        string extension = Path.GetExtension(file).ToLowerInvariant();
        bool supported = motion
          ? extension == ".mp4" || extension == ".webm"
          : extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".webp";
        var info = new FileInfo(file);
        if (!supported || !info.Exists || info.Length <= 0 || info.Length > MaxV2AssetBytes)
          throw new InvalidDataException("v2 素材格式或大小无效：" + relative);
        totalBytes += info.Length;
        if (totalBytes > MaxV2TotalAssetBytes) throw new InvalidDataException("v2 皮肤素材总大小超过限制。");
      }
    }

    private static string GetThemeV2Preview(Dictionary<string, object> raw)
    {
      object value;
      if (!raw.TryGetValue("previews", out value) || value == null) return null;
      object[] previews = value as object[];
      if (previews == null || previews.Length == 0) return null;
      string relative = Convert.ToString(previews[0]).Replace('\\', '/');
      if (!relative.StartsWith("previews/", StringComparison.Ordinal) || !relative.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("v2 预览必须是 previews 目录中的 WebP 文件。");
      return relative;
    }

    private static string FindInstalledPreview(string directory, SkinManifest manifest)
    {
      if (manifest.schemaVersion == 2)
      {
        string converted = Path.Combine(directory, ".manager-preview.png");
        if (File.Exists(converted)) return converted;
        try
        {
          Dictionary<string, object> raw = ParseJsonObject(ReadUtf8(Path.Combine(directory, "theme.json"), 256 * 1024), "theme.json");
          string relative = GetThemeV2Preview(raw);
          if (relative != null)
          {
            string preview = ResolveThemeV2File(directory, relative, "preview");
            if (File.Exists(preview)) return preview;
          }
          object value;
          Dictionary<string, object> assets;
          if (raw.TryGetValue("assets", out value) && (assets = value as Dictionary<string, object>) != null)
          {
            foreach (object candidate in assets.Values)
            {
              string file = ResolveThemeV2File(directory, Convert.ToString(candidate), "asset");
              if (File.Exists(file)) return file;
            }
          }
        }
        catch { }
        return null;
      }
      string v1Preview = Path.Combine(directory, "preview.png");
      return File.Exists(v1Preview) ? v1Preview : Path.Combine(directory, "art.png");
    }

    private void CreateThemeV2ManagerPreview(string directory, SkinManifest manifest)
    {
      string source = FindInstalledPreview(directory, manifest);
      if (string.IsNullOrEmpty(source) || !File.Exists(source)) return;
      if (!string.Equals(Path.GetExtension(source), ".webp", StringComparison.OrdinalIgnoreCase)) return;
      string decoder = Path.Combine(engineRoot, "runtime", "webp", "dwebp.exe");
      if (!File.Exists(decoder)) throw new FileNotFoundException("管理器缺少 WebP 预览解码器。", decoder);
      string output = Path.Combine(directory, ".manager-preview.png");
      var start = new ProcessStartInfo(decoder, QuoteProcessArgument(source) + " -o " + QuoteProcessArgument(output));
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
          throw new TimeoutException("WebP 预览转换超时。");
        }
        if (process.ExitCode != 0 || !File.Exists(output))
          throw new InvalidDataException("WebP 预览转换失败：" + (stderr + " " + stdout).Trim());
      }
      byte[] bytes = File.ReadAllBytes(output);
      if (bytes.Length > MaxPreviewBytes) throw new InvalidDataException("转换后的预览图片过大。");
      ValidatePng(bytes, ".manager-preview.png");
    }

    private static string QuoteProcessArgument(string value)
    {
      return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static void ValidateChrome(string html)
    {
      if (UnsafeChromePattern.IsMatch(html)) throw new InvalidDataException("v2 chrome.html 不允许脚本、事件处理器或远程内容。");
    }

    private static void ValidateCss(string css)
    {
      if (string.IsNullOrWhiteSpace(css)) throw new InvalidDataException("dream-skin.css 不能为空。");
      if (UnsafeCssPattern.IsMatch(css)) throw new InvalidDataException("皮肤 CSS 不允许导入或请求远程内容。");
    }

    private static void ValidatePng(byte[] bytes, string name)
    {
      if (bytes == null || bytes.Length < PngSignature.Length) throw new InvalidDataException(name + " 不是有效 PNG。");
      for (int i = 0; i < PngSignature.Length; i++) if (bytes[i] != PngSignature[i]) throw new InvalidDataException(name + " 不是有效 PNG。");
    }

    private static void ValidateArchiveNames(ZipArchive archive)
    {
      if (archive.Entries.Count == 0 || archive.Entries.Count > MaxArchiveEntries) throw new InvalidDataException("皮肤包文件数量无效。");
      var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      long totalBytes = 0;
      foreach (ZipArchiveEntry entry in archive.Entries)
      {
        string name = entry.FullName.Replace('\\', '/');
        if (name.StartsWith("/", StringComparison.Ordinal) || name.Contains(":") ||
            name.Split('/').Any(delegate(string part) { return part == "." || part == ".."; }))
          throw new InvalidDataException("皮肤包包含不安全路径：" + entry.FullName);
        if (!names.Add(name)) throw new InvalidDataException("皮肤包包含重复路径：" + entry.FullName);
        if (!string.IsNullOrEmpty(entry.Name))
        {
          if (entry.Length <= 0 || entry.Length > MaxV2AssetBytes) throw new InvalidDataException("皮肤包文件大小无效：" + entry.FullName);
          totalBytes += entry.Length;
          if (totalBytes > MaxV2TotalAssetBytes) throw new InvalidDataException("皮肤包解压后总大小超过限制。");
        }
      }
    }

    private static ZipArchiveEntry FindSingleManifest(ZipArchive archive, out bool themeV2)
    {
      List<ZipArchiveEntry> matches = archive.Entries.Where(delegate(ZipArchiveEntry entry)
      {
        string name = entry.FullName.Replace('\\', '/');
        return name.Equals("skin.json", StringComparison.OrdinalIgnoreCase) || name.EndsWith("/skin.json", StringComparison.OrdinalIgnoreCase) ||
          name.Equals("theme.json", StringComparison.OrdinalIgnoreCase) || name.EndsWith("/theme.json", StringComparison.OrdinalIgnoreCase);
      }).ToList();
      if (matches.Count != 1) throw new InvalidDataException("皮肤包必须且只能包含一个 skin.json 或 theme.json。");
      themeV2 = matches[0].FullName.Replace('\\', '/').EndsWith("theme.json", StringComparison.OrdinalIgnoreCase);
      return matches[0];
    }

    private static void ExtractThemeV2Package(ZipArchive archive, string prefix, string stage)
    {
      string normalizedPrefix = prefix.Replace('\\', '/');
      foreach (ZipArchiveEntry entry in archive.Entries)
      {
        string name = entry.FullName.Replace('\\', '/');
        if (!name.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase)) continue;
        string relative = name.Substring(normalizedPrefix.Length);
        if (relative.Length == 0) continue;
        string target = ResolveThemeV2File(stage, relative, "archive entry");
        if (string.IsNullOrEmpty(entry.Name))
        {
          Directory.CreateDirectory(target);
          continue;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        WriteBytes(target, ReadEntry(entry, (int)MaxV2AssetBytes));
      }
      if (!File.Exists(Path.Combine(stage, "theme.json"))) throw new InvalidDataException("v2 皮肤包缺少 theme.json。");
    }

    private static ZipArchiveEntry FindEntry(ZipArchive archive, string expected, bool required)
    {
      List<ZipArchiveEntry> matches = archive.Entries.Where(delegate(ZipArchiveEntry item)
      {
        return string.Equals(item.FullName.Replace('\\', '/'), expected, StringComparison.OrdinalIgnoreCase);
      }).ToList();
      if (matches.Count > 1 || (required && matches.Count == 0)) throw new InvalidDataException("皮肤包缺少或重复文件：" + expected);
      return matches.Count == 0 ? null : matches[0];
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry, int maximumBytes)
    {
      if (entry == null || entry.Length <= 0 || entry.Length > maximumBytes) throw new InvalidDataException("皮肤文件大小无效。");
      using (Stream input = entry.Open())
      using (var output = new MemoryStream())
      {
        input.CopyTo(output);
        if (output.Length > maximumBytes) throw new InvalidDataException("皮肤文件超过大小限制。");
        return output.ToArray();
      }
    }

    private static string DecodeUtf8(byte[] bytes)
    {
      try { return new UTF8Encoding(false, true).GetString(bytes); }
      catch (DecoderFallbackException error) { throw new InvalidDataException("皮肤文本必须是 UTF-8。", error); }
    }

    private static string ReadUtf8(string path, int maximumBytes)
    {
      var info = new FileInfo(path);
      if (!info.Exists || info.Length <= 0 || info.Length > maximumBytes) throw new InvalidDataException("皮肤文件不存在或大小无效：" + Path.GetFileName(path));
      return DecodeUtf8(File.ReadAllBytes(path));
    }

    private static void WriteUtf8Atomically(string path, string content)
    {
      string temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
      string backup = path + ".replace-backup";
      File.WriteAllText(temp, content, new UTF8Encoding(false));
      try
      {
        if (File.Exists(path))
        {
          if (File.Exists(backup)) File.Delete(backup);
          File.Replace(temp, path, backup, true);
          if (File.Exists(backup)) File.Delete(backup);
        }
        else File.Move(temp, path);
      }
      finally
      {
        if (File.Exists(temp)) File.Delete(temp);
        if (File.Exists(backup)) File.Delete(backup);
      }
    }

    private static string CleanText(string value, string fallback, int maximumLength)
    {
      string clean = string.IsNullOrWhiteSpace(value) ? fallback : Regex.Replace(value.Trim(), "[\\x00-\\x1f\\x7f]", " ");
      return clean.Length > maximumLength ? clean.Substring(0, maximumLength) : clean;
    }

    private static bool IsValidSkinId(string id)
    {
      return id != null && SkinIdPattern.IsMatch(id);
    }

    private string GetSkinDirectory(string id)
    {
      if (!IsValidSkinId(id)) throw new InvalidDataException("皮肤 ID 无效。");
      string full = Path.GetFullPath(Path.Combine(skinsRoot, id));
      string prefix = skinsRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
      if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("皮肤目录越界。");
      return full;
    }

    private static bool PathsEqual(string left, string right)
    {
      return string.Equals(Path.GetFullPath(left).TrimEnd('\\'), Path.GetFullPath(right).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyFile(string source, string destination)
    {
      if (!File.Exists(source)) throw new FileNotFoundException("引擎缺少内置皮肤文件。", source);
      File.Copy(source, destination, true);
    }

    private static void WriteBytes(string path, byte[] bytes)
    {
      File.WriteAllBytes(path, bytes);
    }

    private static void AddFile(ZipArchive archive, string source, string name)
    {
      ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
      using (Stream input = File.OpenRead(source))
      using (Stream output = entry.Open()) input.CopyTo(output);
    }

    private static void AddDirectoryFiles(ZipArchive archive, string directory)
    {
      string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
      foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
      {
        if (string.Equals(Path.GetFileName(file), ".manager-preview.png", StringComparison.OrdinalIgnoreCase)) continue;
        string relative = Path.GetFullPath(file).Substring(root.Length).Replace(Path.DirectorySeparatorChar, '/');
        AddFile(archive, file, relative);
      }
    }

    private static void AddText(ZipArchive archive, string content, string name)
    {
      ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
      using (Stream output = entry.Open())
      using (var writer = new StreamWriter(output, new UTF8Encoding(false))) writer.Write(content);
    }
  }
}
