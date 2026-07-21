using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace CodexDreamSkinManager
{
  internal static class CodexClientMessages
  {
    public static string Title(bool isRunning)
    {
      return isRunning ? "Codex 正在运行" : "Codex 未运行";
    }

    public static string Apply(string skinName, bool isRunning)
    {
      return isRunning
        ? "检测到 Codex 客户端正在运行。\n\n管理器会优先热更新皮肤；如果当前会话未启用皮肤接口，将关闭并重新启动 Codex。未保存的输入可能丢失。\n\n是否应用“" + skinName + "”？"
        : "未检测到正在运行的 Codex 客户端。\n\n应用“" + skinName + "”后将自动启动 Codex。\n\n是否继续？";
    }

    public static string Restore(bool isRunning)
    {
      return isRunning
        ? "检测到 Codex 客户端正在运行。\n\n恢复官方外观会关闭并重新启动 Codex；已导入的皮肤仍会保留。\n\n是否继续？"
        : "未检测到正在运行的 Codex 客户端。\n\n将恢复官方外观，Codex 会保持关闭；已导入的皮肤仍会保留。\n\n是否继续？";
    }
  }

  public sealed class MainForm : Form
  {
    private static readonly Color Canvas = Color.FromArgb(247, 244, 239);
    private static readonly Color Surface = Color.FromArgb(255, 253, 249);
    private static readonly Color SurfaceAlt = Color.FromArgb(248, 242, 236);
    private static readonly Color Ink = Color.FromArgb(60, 45, 48);
    private static readonly Color Muted = Color.FromArgb(113, 94, 97);
    private static readonly Color Border = Color.FromArgb(226, 213, 205);
    private static readonly Color Coral = Color.FromArgb(206, 103, 82);
    private static readonly Color CoralDark = Color.FromArgb(174, 73, 59);
    private static readonly Color Teal = Color.FromArgb(61, 132, 137);
    private static readonly Color Danger = Color.FromArgb(176, 61, 61);

    private readonly string screenshotPath;
    private string engineRoot;
    private SkinStore store;
    private EngineRunner runner;
    private SkinListBox skinList;
    private PictureBox preview;
    private Label titleLabel;
    private Label descriptionLabel;
    private Label metadataLabel;
    private PillLabel activeLabel;
    private PillLabel libraryCountLabel;
    private PillLabel codexStatusLabel;
    private Label starlightStatusLabel;
    private ToolStripStatusLabel statusLabel;
    private Timer codexStatusTimer;
    private Button importButton;
    private Button applyButton;
    private Button renameButton;
    private Button exportButton;
    private Button folderButton;
    private Button deleteButton;
    private Button restoreButton;
    private DreamToggle starlightToggle;
    private SplitContainer mainSplit;
    private bool busy;
    private bool updatingStarlightToggle;
    private bool? lastCodexRunning;

    public MainForm(string screenshotPath)
    {
      this.screenshotPath = screenshotPath;
      Text = "Codex皮肤主题管理器";
      try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
      Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
      BackColor = Canvas;
      ForeColor = Ink;
      StartPosition = FormStartPosition.CenterScreen;
      MinimumSize = new Size(1000, 700);
      ClientSize = new Size(1160, 760);
      KeyPreview = true;
      AllowDrop = true;
      AutoScaleMode = AutoScaleMode.Dpi;

      BuildInterface();
      DragEnter += OnDragEnter;
      DragDrop += OnDragDrop;
      Shown += OnShown;
      KeyDown += OnKeyDown;
      Resize += delegate { AdjustSplitForWindow(false); };
      InitializeEngine();
      codexStatusTimer = new Timer { Interval = 6000 };
      codexStatusTimer.Tick += delegate { RefreshCodexStatusIndicator(); };
      codexStatusTimer.Start();
    }

    private void BuildInterface()
    {
      GradientHeaderPanel header = new GradientHeaderPanel { Dock = DockStyle.Fill };
      BrandMark brandMark = new BrandMark(Icon) { Location = new Point(28, 27), Size = new Size(54, 54) };
      Label appTitle = new Label
      {
        AutoSize = true,
        Text = "Codex皮肤主题管理器",
        Font = new Font("Segoe UI", 23F, FontStyle.Bold),
        ForeColor = Ink,
        BackColor = Color.Transparent,
        Location = new Point(96, 20)
      };
      Label appSubtitle = new Label
      {
        AutoSize = true,
        Text = "收藏、预览并一键切换你的 Codex 专属外观",
        Font = new Font("Segoe UI", 10F),
        ForeColor = Muted,
        BackColor = Color.Transparent,
        Location = new Point(99, 62)
      };
      Label importHint = new Label
      {
        AutoSize = true,
        Text = "支持拖放 .codexskin / ZIP",
        Font = new Font("Segoe UI", 8.5F),
        ForeColor = Muted,
        BackColor = Color.Transparent
      };
      codexStatusLabel = new PillLabel
      {
        Text = "正在检测...",
        FillColor = Color.FromArgb(235, 231, 226),
        TextColor = Muted,
        Size = new Size(108, 26)
      };
      RoundedPanel starlightPanel = new RoundedPanel
      {
        BackColor = Color.FromArgb(255, 251, 247),
        BorderColor = Color.FromArgb(224, 205, 195),
        Radius = 13,
        Size = new Size(220, 54)
      };
      Label starlightIcon = new Label
      {
        AutoSize = false,
        Text = "✦",
        TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Segoe UI Symbol", 14F, FontStyle.Bold),
        ForeColor = Coral,
        BackColor = Color.Transparent,
        Location = new Point(10, 8),
        Size = new Size(30, 38)
      };
      Label starlightTitle = new Label
      {
        AutoSize = true,
        Text = "动态特效",
        Font = new Font("Segoe UI", 9.2F, FontStyle.Bold),
        ForeColor = Ink,
        BackColor = Color.Transparent,
        Location = new Point(43, 8)
      };
      starlightStatusLabel = new Label
      {
        AutoSize = true,
        Text = "星光已开启",
        Font = new Font("Segoe UI", 8F),
        ForeColor = Teal,
        BackColor = Color.Transparent,
        Location = new Point(43, 29)
      };
      starlightToggle = new DreamToggle
      {
        Checked = true,
        Location = new Point(145, 11),
        Size = new Size(62, 32),
        AccessibleName = "动态特效总开关",
        AccessibleDescription = "打开或关闭所有皮肤中的星光动态特效"
      };
      starlightToggle.CheckedChanged += OnStarlightToggleChanged;
      starlightPanel.Controls.Add(starlightIcon);
      starlightPanel.Controls.Add(starlightTitle);
      starlightPanel.Controls.Add(starlightStatusLabel);
      starlightPanel.Controls.Add(starlightToggle);
      importButton = CreateButton("导入皮肤", true, 136);
      importButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
      importButton.Location = new Point(20, 31);
      importButton.Click += delegate { ImportSkin(); };
      header.Controls.Add(brandMark);
      header.Controls.Add(appTitle);
      header.Controls.Add(appSubtitle);
      header.Controls.Add(codexStatusLabel);
      header.Controls.Add(starlightPanel);
      header.Controls.Add(importHint);
      header.Controls.Add(importButton);
      header.Resize += delegate
      {
        importButton.Location = new Point(Math.Max(20, header.ClientSize.Width - importButton.Width - 30), 31);
        importHint.Location = new Point(importButton.Left + Math.Max(0, (importButton.Width - importHint.Width) / 2), 78);
        starlightPanel.Location = new Point(Math.Max(470, importButton.Left - starlightPanel.Width - 16), 27);
        codexStatusLabel.Visible = header.ClientSize.Width >= 1100;
        codexStatusLabel.Location = new Point(Math.Max(350, starlightPanel.Left - codexStatusLabel.Width - 14), 41);
      };

      statusLabel = new ToolStripStatusLabel
      {
        Text = "正在准备皮肤库...",
        ForeColor = Muted,
        Spring = true,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(16, 0, 0, 0)
      };
      ToolStripStatusLabel statusDot = new ToolStripStatusLabel
      {
        Text = "●",
        ForeColor = Teal,
        Padding = new Padding(12, 0, 0, 0)
      };
      StatusStrip statusStrip = new StatusStrip
      {
        Dock = DockStyle.Fill,
        BackColor = Surface,
        ForeColor = Muted,
        SizingGrip = false,
        RenderMode = ToolStripRenderMode.System,
        Padding = new Padding(0)
      };
      statusStrip.Items.Add(statusDot);
      statusStrip.Items.Add(statusLabel);
      statusStrip.Paint += delegate(object sender, PaintEventArgs args)
      {
        using (var pen = new Pen(Color.FromArgb(235, 225, 218))) args.Graphics.DrawLine(pen, 0, 0, statusStrip.Width, 0);
      };

      mainSplit = new SplitContainer
      {
        Dock = DockStyle.Fill,
        SplitterWidth = 1,
        FixedPanel = FixedPanel.Panel1,
        IsSplitterFixed = false,
        BackColor = Border
      };
      mainSplit.Panel1.BackColor = SurfaceAlt;
      mainSplit.Panel2.BackColor = Canvas;
      mainSplit.Panel1MinSize = 270;
      BuildLibraryPanel(mainSplit.Panel1);
      BuildDetailsPanel(mainSplit.Panel2);

      TableLayoutPanel shell = new TableLayoutPanel
      {
        Dock = DockStyle.Fill,
        ColumnCount = 1,
        RowCount = 3,
        Margin = new Padding(0),
        Padding = new Padding(0),
        BackColor = Canvas
      };
      shell.ColumnStyles.Clear();
      shell.RowStyles.Clear();
      shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
      shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 108F));
      shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
      shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
      shell.Controls.Add(header, 0, 0);
      shell.Controls.Add(mainSplit, 0, 1);
      shell.Controls.Add(statusStrip, 0, 2);
      Controls.Add(shell);
    }

    private void BuildLibraryPanel(Control panel)
    {
      Panel heading = new Panel { Dock = DockStyle.Top, Height = 92, Padding = new Padding(24, 20, 18, 0), BackColor = Color.Transparent };
      Label label = new Label
      {
        AutoSize = true,
        Text = "我的皮肤",
        Font = new Font("Segoe UI", 13F, FontStyle.Bold),
        ForeColor = Ink,
        BackColor = Color.Transparent,
        Location = new Point(24, 18)
      };
      libraryCountLabel = new PillLabel
      {
        Text = "0 个皮肤",
        FillColor = Color.FromArgb(230, 242, 240),
        TextColor = Teal,
        Location = new Point(24, 51),
        Size = new Size(78, 25)
      };
      Label hint = new Label
      {
        AutoSize = true,
        Text = "双击即可应用",
        Font = new Font("Segoe UI", 8.5F),
        ForeColor = Muted,
        BackColor = Color.Transparent,
        Anchor = AnchorStyles.Top | AnchorStyles.Right,
        Location = new Point(176, 56)
      };
      heading.Controls.Add(label);
      heading.Controls.Add(libraryCountLabel);
      heading.Controls.Add(hint);
      heading.Resize += delegate { hint.Left = Math.Max(112, heading.ClientSize.Width - hint.Width - 20); };

      skinList = new SkinListBox
      {
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None,
        BackColor = SurfaceAlt,
        ForeColor = Ink,
        IntegralHeight = false,
        Margin = new Padding(12)
      };
      skinList.SelectedIndexChanged += delegate { ShowSelectedSkin(); };
      skinList.DoubleClick += delegate { ApplySelectedSkin(); };

      Panel listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 0, 14, 18), BackColor = Color.Transparent };
      listHost.Controls.Add(skinList);
      panel.Controls.Add(listHost);
      panel.Controls.Add(heading);
    }

    private void BuildDetailsPanel(Control panel)
    {
      Panel content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28, 24, 28, 18), BackColor = Canvas };

      PreviewHost previewHost = new PreviewHost { Dock = DockStyle.Top, Height = 316, BackColor = Surface, Padding = new Padding(2) };
      preview = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(239, 234, 228), SizeMode = PictureBoxSizeMode.Zoom };
      PillLabel previewTag = new PillLabel
      {
        Text = "主题预览",
        FillColor = Color.FromArgb(224, 238, 237),
        TextColor = Teal,
        Location = new Point(16, 16),
        Size = new Size(76, 26),
        Anchor = AnchorStyles.Top | AnchorStyles.Left
      };
      previewHost.Controls.Add(preview);
      previewHost.Controls.Add(previewTag);
      previewTag.BringToFront();

      Panel actionPanel = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Color.Transparent };
      FlowLayoutPanel primaryActions = new FlowLayoutPanel
      {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = false,
        BackColor = Color.Transparent,
        Padding = new Padding(0, 8, 0, 0)
      };
      applyButton = CreateButton("应用并启动", true, 130);
      renameButton = CreateButton("重命名", false, 82);
      exportButton = CreateButton("导出皮肤包", false, 108);
      folderButton = CreateButton("打开目录", false, 90);
      deleteButton = CreateButton("删除", false, 68);
      ((DreamButton)deleteButton).Danger = true;
      applyButton.Click += delegate { ApplySelectedSkin(); };
      renameButton.Click += delegate { RenameSelectedSkin(); };
      exportButton.Click += delegate { ExportSelectedSkin(); };
      folderButton.Click += delegate { OpenSelectedFolder(); };
      deleteButton.Click += delegate { DeleteSelectedSkin(); };
      primaryActions.Controls.Add(applyButton);
      primaryActions.Controls.Add(renameButton);
      primaryActions.Controls.Add(exportButton);
      primaryActions.Controls.Add(folderButton);
      primaryActions.Controls.Add(deleteButton);
      restoreButton = CreateButton("恢复官方外观", false, 120);
      restoreButton.Margin = new Padding(6, 0, 0, 0);
      restoreButton.Click += delegate { RestoreOfficialAppearance(); };
      primaryActions.Controls.Add(restoreButton);
      actionPanel.Controls.Add(primaryActions);

      Panel detailsHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 16, 0, 12), BackColor = Color.Transparent };
      RoundedPanel details = new RoundedPanel
      {
        Dock = DockStyle.Fill,
        BackColor = Surface,
        BorderColor = Border,
        Radius = 16
      };
      activeLabel = new PillLabel
      {
        Text = "当前使用",
        FillColor = Teal,
        TextColor = Color.White,
        Location = new Point(20, 17),
        Size = new Size(82, 26),
        Visible = true
      };
      titleLabel = new Label
      {
        AutoEllipsis = true,
        Font = new Font("Segoe UI", 19.5F, FontStyle.Bold),
        ForeColor = Ink,
        BackColor = Color.Transparent,
        Location = new Point(20, 49),
        Size = new Size(600, 38),
        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
      };
      descriptionLabel = new Label
      {
        AutoEllipsis = true,
        Font = new Font("Segoe UI", 10F),
        ForeColor = Muted,
        BackColor = Color.Transparent,
        Location = new Point(22, 89),
        Size = new Size(600, 28),
        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
      };
      metadataLabel = new Label
      {
        AutoEllipsis = true,
        Font = new Font("Segoe UI", 9F),
        ForeColor = Teal,
        BackColor = Color.Transparent,
        Location = new Point(22, 121),
        Size = new Size(600, 24),
        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
      };
      details.Controls.Add(activeLabel);
      details.Controls.Add(titleLabel);
      details.Controls.Add(descriptionLabel);
      details.Controls.Add(metadataLabel);
      details.Resize += delegate
      {
        int labelWidth = Math.Max(120, details.ClientSize.Width - 44);
        titleLabel.Width = labelWidth;
        descriptionLabel.Width = labelWidth;
        metadataLabel.Width = labelWidth;
      };
      detailsHost.Controls.Add(details);

      content.Controls.Add(detailsHost);
      content.Controls.Add(actionPanel);
      content.Controls.Add(previewHost);
      content.Resize += delegate
      {
        previewHost.Height = Math.Max(230, Math.Min(320, content.ClientSize.Height - 300));
      };
      panel.Controls.Add(content);
    }

    private Button CreateButton(string text, bool primary, int width)
    {
      var button = new DreamButton
      {
        Text = text,
        Width = width,
        Height = 44,
        Cursor = Cursors.Hand,
        Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
        Margin = new Padding(0, 0, 8, 0),
        AccessibleName = text,
        Primary = primary
      };
      return button;
    }

    private void InitializeEngine()
    {
      try
      {
        engineRoot = EngineLocator.FindEngineRoot();
        if (engineRoot == null) throw new DirectoryNotFoundException("找不到皮肤引擎。请把管理器放回完整解压目录后运行。");
        string storageRoot = ResolveStorageRoot(engineRoot);
        store = new SkinStore(engineRoot, storageRoot);
        runner = new EngineRunner(engineRoot, storageRoot);
        SyncStarlightToggle();
        RefreshSkins(null);
        SetStatus("就绪 · PowerShell: " + runner.PowerShellPath);
        RefreshCodexStatusIndicator();
      }
      catch (Exception error)
      {
        SetStatus("引擎不可用：" + error.Message);
        SetControlsEnabled(false);
        importButton.Enabled = false;
      }
    }

    private static string ResolveStorageRoot(string engineRoot)
    {
      string overrideRoot = Environment.GetEnvironmentVariable("CODEX_DREAM_SKIN_STATE_ROOT");
      if (!string.IsNullOrWhiteSpace(overrideRoot)) return Path.GetFullPath(overrideRoot);
      if (string.Equals(Path.GetFileName(engineRoot.TrimEnd(Path.DirectorySeparatorChar)), ".codex-dream-skin", StringComparison.OrdinalIgnoreCase))
      {
        DirectoryInfo parent = Directory.GetParent(engineRoot);
        if (parent != null) return parent.FullName;
      }
      return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexDreamSkin");
    }

    private CodexClientStatus RefreshCodexStatusIndicator()
    {
      CodexClientStatus status = CodexClientDetector.Detect();
      if (codexStatusLabel != null && (!lastCodexRunning.HasValue || lastCodexRunning.Value != status.IsRunning))
      {
        lastCodexRunning = status.IsRunning;
        codexStatusLabel.Text = status.IsRunning ? "Codex 运行中" : "Codex 未运行";
        codexStatusLabel.FillColor = status.IsRunning
          ? Color.FromArgb(224, 238, 237)
          : Color.FromArgb(246, 226, 218);
        codexStatusLabel.TextColor = status.IsRunning ? Teal : CoralDark;
        codexStatusLabel.AccessibleName = status.IsRunning ? "Codex 客户端正在运行" : "Codex 客户端未运行";
        codexStatusLabel.Invalidate();
      }
      return status;
    }

    private SkinRecord SelectedSkin
    {
      get { return skinList == null ? null : skinList.SelectedItem as SkinRecord; }
    }

    private void RefreshSkins(string selectId)
    {
      List<SkinRecord> skins = store.LoadSkins();
      SyncStarlightToggle();
      skinList.BeginUpdate();
      skinList.Items.Clear();
      foreach (SkinRecord skin in skins) skinList.Items.Add(skin);
      skinList.EndUpdate();
      libraryCountLabel.Text = skins.Count + " 个皮肤";
      int index = 0;
      if (!string.IsNullOrEmpty(selectId))
      {
        for (int i = 0; i < skins.Count; i++) if (skins[i].Manifest.id == selectId) index = i;
      }
      if (skinList.Items.Count > 0) skinList.SelectedIndex = index;
      skinList.Invalidate();
    }

    private void ShowSelectedSkin()
    {
      SkinRecord skin = SelectedSkin;
      if (skin == null)
      {
        titleLabel.Text = "未选择皮肤";
        descriptionLabel.Text = string.Empty;
        metadataLabel.Text = string.Empty;
        activeLabel.Visible = false;
        SetActionAvailability(false);
        ReplacePreview(null);
        if (starlightToggle != null) starlightToggle.Enabled = !busy && store != null;
        return;
      }
      titleLabel.Text = skin.Manifest.name;
      descriptionLabel.Text = skin.Manifest.description;
      metadataLabel.Text = "作者  " + skin.Manifest.author + "     版本  " + skin.Manifest.version +
        "     格式  v" + skin.Manifest.schemaVersion + "     ID  " + skin.Manifest.id;
      activeLabel.Text = skin.IsActive ? "当前使用" : "可以切换";
      activeLabel.FillColor = skin.IsActive ? Teal : Color.FromArgb(246, 226, 218);
      activeLabel.TextColor = skin.IsActive ? Color.White : CoralDark;
      activeLabel.Visible = true;
      activeLabel.Invalidate();
      activeLabel.BringToFront();
      SetActionAvailability(true);
      renameButton.Enabled = !busy;
      deleteButton.Enabled = !skin.IsBuiltIn && !skin.IsActive && !busy;
      ReplacePreview(skin.PreviewPath);
    }

    private void SyncStarlightToggle()
    {
      if (starlightToggle == null || store == null) return;
      updatingStarlightToggle = true;
      try
      {
        bool enabled = store.GetStarlightEnabled();
        starlightToggle.Checked = enabled;
        starlightToggle.Text = enabled ? "开" : "关";
        if (starlightStatusLabel != null)
        {
          starlightStatusLabel.Text = enabled ? "星光已开启" : "星光已关闭";
          starlightStatusLabel.ForeColor = enabled ? Teal : Muted;
        }
        starlightToggle.Enabled = !busy;
      }
      finally { updatingStarlightToggle = false; }
    }

    private void OnStarlightToggleChanged(object sender, EventArgs args)
    {
      if (updatingStarlightToggle || store == null) return;
      try
      {
        bool enabled = starlightToggle.Checked;
        starlightToggle.Text = enabled ? "开" : "关";
        if (starlightStatusLabel != null)
        {
          starlightStatusLabel.Text = enabled ? "星光已开启" : "星光已关闭";
          starlightStatusLabel.ForeColor = enabled ? Teal : Muted;
        }
        store.SetStarlightEnabled(enabled);
        CodexClientStatus codexStatus = RefreshCodexStatusIndicator();
        if (!codexStatus.IsRunning)
        {
          SetStatus("已" + (enabled ? "开启" : "关闭") + "动态特效，下次启动 Codex 时生效。");
          return;
        }
        RunEngineAction("正在" + (enabled ? "开启" : "关闭") + "动态特效...",
          delegate { return runner.ApplySelectedSkin(false); },
          delegate(RunResult result)
          {
            if (result.ExitCode == 0)
              SetStatus("动态特效已" + (enabled ? "开启" : "关闭") + "，当前 Codex 已更新。");
            else
              SetStatus("动态特效设置已保存；重新应用皮肤或重启 Codex 后生效。");
          });
      }
      catch (Exception error)
      {
        SyncStarlightToggle();
        ShowError("无法保存星光设置", error);
      }
    }

    private void ReplacePreview(string path)
    {
      Image previous = preview.Image;
      preview.Image = null;
      if (previous != null) previous.Dispose();
      if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
      try
      {
        using (Image source = Image.FromFile(path)) preview.Image = new Bitmap(source);
      }
      catch { }
    }

    private void ImportSkin()
    {
      using (var dialog = new OpenFileDialog())
      {
        dialog.Title = "导入 Codex 皮肤包";
        dialog.Filter = "Codex 皮肤包 (*.codexskin;*.zip)|*.codexskin;*.zip|所有文件 (*.*)|*.*";
        dialog.Multiselect = false;
        if (dialog.ShowDialog(this) == DialogResult.OK) ImportSkinFile(dialog.FileName);
      }
    }

    private void ImportSkinFile(string fileName)
    {
      try
      {
        bool overwrite = false;
        try
        {
          SkinRecord imported = store.ImportSkin(fileName, false);
          RefreshSkins(imported.Manifest.id);
          SetStatus("已导入皮肤：" + imported.Manifest.name);
          return;
        }
        catch (IOException error)
        {
          DialogResult answer = DreamDialog.Confirm(this, "皮肤已存在",
            error.Message + Environment.NewLine + "替换后将使用新皮肤包中的内容。",
            "替换皮肤", "保留现有", DreamDialogTone.Question, false, false);
          overwrite = answer == DialogResult.Yes;
          if (!overwrite) return;
        }
        SkinRecord replaced = store.ImportSkin(fileName, true);
        RefreshSkins(replaced.Manifest.id);
        SetStatus("已更新皮肤：" + replaced.Manifest.name);
      }
      catch (Exception error) { ShowError("导入失败", error); }
    }

    private void ApplySelectedSkin()
    {
      SkinRecord skin = SelectedSkin;
      if (skin == null || busy) return;
      CodexClientStatus codexStatus = RefreshCodexStatusIndicator();
      string confirmation = CodexClientMessages.Apply(skin.Manifest.name, codexStatus.IsRunning);
      DialogResult confirmationResult = DreamDialog.Confirm(this,
        CodexClientMessages.Title(codexStatus.IsRunning), confirmation,
        "应用皮肤", "暂不应用",
        codexStatus.IsRunning ? DreamDialogTone.Warning : DreamDialogTone.Information,
        false, !codexStatus.IsRunning);
      if (confirmationResult != DialogResult.Yes)
      {
        SetStatus("已取消应用皮肤。");
        return;
      }
      try
      {
        store.SetActiveSkin(skin.Manifest.id);
        RefreshSkins(skin.Manifest.id);
      }
      catch (Exception error)
      {
        ShowError("无法选择皮肤", error);
        return;
      }
      RunEngineAction("正在应用 " + skin.Manifest.name + "...", delegate { return runner.ApplySelectedSkin(codexStatus.IsRunning); },
        delegate(RunResult result)
        {
          if (result.ExitCode != 0) throw new InvalidOperationException(ResultMessage(result));
          SetStatus("已应用 " + skin.Manifest.name + "。刷新或重启后的 Codex 会继续使用它。");
        });
    }

    private void ExportSelectedSkin()
    {
      SkinRecord skin = SelectedSkin;
      if (skin == null || busy) return;
      using (var dialog = new SaveFileDialog())
      {
        dialog.Title = "导出 Codex 皮肤包";
        dialog.Filter = "Codex 皮肤包 (*.codexskin)|*.codexskin";
        dialog.FileName = skin.Manifest.id + "-v" + skin.Manifest.version + ".codexskin";
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
          store.ExportSkin(skin, dialog.FileName);
          SetStatus("已导出：" + dialog.FileName);
        }
        catch (Exception error) { ShowError("导出失败", error); }
      }
    }

    private void RenameSelectedSkin()
    {
      SkinRecord skin = SelectedSkin;
      if (skin == null || busy) return;
      using (var dialog = new RenameSkinDialog(skin.Manifest.name))
      {
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
          SkinRecord renamed = store.RenameSkin(skin, dialog.SkinName);
          RefreshSkins(renamed.Manifest.id);
          SetStatus("已重命名皮肤：" + renamed.Manifest.name);
        }
        catch (Exception error) { ShowError("重命名失败", error); }
      }
    }

    private void OpenSelectedFolder()
    {
      SkinRecord skin = SelectedSkin;
      if (skin == null) return;
      try { Process.Start("explorer.exe", "\"" + skin.DirectoryPath + "\""); }
      catch (Exception error) { ShowError("无法打开目录", error); }
    }

    private void DeleteSelectedSkin()
    {
      SkinRecord skin = SelectedSkin;
      if (skin == null || busy) return;
      DialogResult answer = DreamDialog.Confirm(this, "删除皮肤",
        "确定删除“" + skin.Manifest.name + "”吗？此操作不会影响其他皮肤。",
        "删除", "取消", DreamDialogTone.Danger, true, false);
      if (answer != DialogResult.Yes) return;
      try
      {
        store.DeleteSkin(skin);
        RefreshSkins(null);
        SetStatus("已删除皮肤：" + skin.Manifest.name);
      }
      catch (Exception error) { ShowError("删除失败", error); }
    }

    private void RestoreOfficialAppearance()
    {
      if (busy) return;
      CodexClientStatus codexStatus = RefreshCodexStatusIndicator();
      string confirmation = CodexClientMessages.Restore(codexStatus.IsRunning);
      DialogResult answer = DreamDialog.Confirm(this,
        CodexClientMessages.Title(codexStatus.IsRunning), confirmation,
        "恢复官方外观", "取消",
        codexStatus.IsRunning ? DreamDialogTone.Warning : DreamDialogTone.Information,
        false, false);
      if (answer != DialogResult.Yes) return;
      RunEngineAction("正在恢复官方外观...", delegate { return runner.RestoreOfficialAppearance(codexStatus.IsRunning); },
        delegate(RunResult result)
        {
          if (result.ExitCode != 0) throw new InvalidOperationException(ResultMessage(result));
          SetStatus("官方外观已恢复。皮肤库内容未删除。");
        });
    }

    private void RunEngineAction(string workingStatus, Func<RunResult> operation, Action<RunResult> completed)
    {
      if (busy) return;
      busy = true;
      SetControlsEnabled(false);
      SetStatus(workingStatus);
      var worker = new BackgroundWorker();
      worker.DoWork += delegate(object sender, DoWorkEventArgs args) { args.Result = operation(); };
      worker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs args)
      {
        busy = false;
        SetControlsEnabled(true);
        RefreshCodexStatusIndicator();
        ShowSelectedSkin();
        if (args.Error != null) ShowError("操作失败", args.Error);
        else
        {
          try { completed((RunResult)args.Result); }
          catch (Exception error) { ShowError("操作失败", error); }
        }
        worker.Dispose();
      };
      worker.RunWorkerAsync();
    }

    private void SetControlsEnabled(bool enabled)
    {
      importButton.Enabled = enabled;
      applyButton.Enabled = enabled && SelectedSkin != null;
      renameButton.Enabled = enabled && SelectedSkin != null;
      exportButton.Enabled = enabled && SelectedSkin != null;
      folderButton.Enabled = enabled && SelectedSkin != null;
      deleteButton.Enabled = enabled && SelectedSkin != null && !SelectedSkin.IsBuiltIn && !SelectedSkin.IsActive;
      restoreButton.Enabled = enabled;
      skinList.Enabled = enabled;
      if (starlightToggle != null) starlightToggle.Enabled = enabled && store != null;
    }

    private void SetActionAvailability(bool selected)
    {
      applyButton.Enabled = selected && !busy && runner != null;
      renameButton.Enabled = selected && !busy && SelectedSkin != null;
      exportButton.Enabled = selected && !busy;
      folderButton.Enabled = selected && !busy;
      deleteButton.Enabled = selected && !busy;
      restoreButton.Enabled = !busy && runner != null;
      if (starlightToggle != null) starlightToggle.Enabled = !busy && store != null;
    }

    private void SetStatus(string value)
    {
      statusLabel.Text = value;
    }

    private void ShowError(string title, Exception error)
    {
      SetStatus(title + "：" + error.Message);
      DreamDialog.ShowMessage(this, title, error.Message, DreamDialogTone.Danger);
    }

    private static string ResultMessage(RunResult result)
    {
      return string.IsNullOrWhiteSpace(result.Output) ? "皮肤引擎退出，代码 " + result.ExitCode : result.Output;
    }

    private void OnDragEnter(object sender, DragEventArgs args)
    {
      if (args.Data.GetDataPresent(DataFormats.FileDrop)) args.Effect = DragDropEffects.Copy;
    }

    private void OnDragDrop(object sender, DragEventArgs args)
    {
      string[] files = args.Data.GetData(DataFormats.FileDrop) as string[];
      if (files != null && files.Length == 1) ImportSkinFile(files[0]);
    }

    private void OnKeyDown(object sender, KeyEventArgs args)
    {
      if (args.Control && args.KeyCode == Keys.O)
      {
        args.Handled = true;
        ImportSkin();
      }
      else if (args.KeyCode == Keys.F2)
      {
        args.Handled = true;
        RenameSelectedSkin();
      }
      else if (args.KeyCode == Keys.Enter && skinList.Focused)
      {
        args.Handled = true;
        ApplySelectedSkin();
      }
    }

    private void OnShown(object sender, EventArgs args)
    {
      AdjustSplitForWindow(true);
      if (string.IsNullOrEmpty(screenshotPath)) return;
      var timer = new Timer { Interval = 900 };
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
        Close();
      };
      timer.Start();
    }

    private void AdjustSplitForWindow(bool initialize)
    {
      if (mainSplit == null || mainSplit.Width <= 0) return;
      int preferred = ClientSize.Width < 1080
        ? 270
        : Math.Min(310, Math.Max(270, ClientSize.Width / 3));
      if (mainSplit.Width > preferred + 500 && (initialize || ClientSize.Width < 1080))
        mainSplit.SplitterDistance = preferred;
    }

    protected override void Dispose(bool disposing)
    {
      if (disposing)
      {
        if (codexStatusTimer != null) { codexStatusTimer.Stop(); codexStatusTimer.Dispose(); codexStatusTimer = null; }
        if (preview != null && preview.Image != null) preview.Image.Dispose();
      }
      base.Dispose(disposing);
    }
  }

  internal static class UiGeometry
  {
    public static GraphicsPath Rounded(Rectangle rectangle, int radius)
    {
      int safeRadius = Math.Max(1, Math.Min(radius, Math.Min(rectangle.Width, rectangle.Height) / 2));
      int diameter = safeRadius * 2;
      var path = new GraphicsPath();
      path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
      path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
      path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
      path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
      path.CloseFigure();
      return path;
    }

    public static Control BackgroundAncestor(Control control, out int offsetX, out int offsetY)
    {
      offsetX = control.Left;
      offsetY = control.Top;
      Control ancestor = control.Parent;
      while (ancestor != null && ancestor.BackColor.A < 255)
      {
        offsetX += ancestor.Left;
        offsetY += ancestor.Top;
        ancestor = ancestor.Parent;
      }
      return ancestor;
    }
  }

  internal sealed class GradientHeaderPanel : Panel
  {
    public GradientHeaderPanel()
    {
      SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    protected override void OnPaintBackground(PaintEventArgs args)
    {
      args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
      using (var gradient = new LinearGradientBrush(ClientRectangle,
        Color.FromArgb(255, 249, 244), Color.FromArgb(237, 246, 244), LinearGradientMode.Horizontal))
      {
        args.Graphics.FillRectangle(gradient, ClientRectangle);
      }
      using (var warm = new SolidBrush(Color.FromArgb(36, 225, 145, 119)))
        args.Graphics.FillEllipse(warm, Width - 350, -125, 320, 260);
      using (var cool = new SolidBrush(Color.FromArgb(28, 89, 164, 168)))
        args.Graphics.FillEllipse(cool, Width - 170, -90, 230, 210);
    }

    protected override void OnPaint(PaintEventArgs args)
    {
      base.OnPaint(args);
      using (var pen = new Pen(Color.FromArgb(226, 213, 205)))
        args.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }
  }

  internal class RoundedPanel : Panel
  {
    private int radius = 16;
    private Color borderColor = Color.FromArgb(226, 213, 205);

    public RoundedPanel()
    {
      SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
        ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
      BackColor = Color.White;
    }

    public int Radius
    {
      get { return radius; }
      set { radius = Math.Max(1, value); UpdateShape(); Invalidate(); }
    }

    public Color BorderColor
    {
      get { return borderColor; }
      set { borderColor = value; Invalidate(); }
    }

    protected override void OnResize(EventArgs eventArgs)
    {
      base.OnResize(eventArgs);
      UpdateShape();
    }

    private void UpdateShape()
    {
      if (Width <= 1 || Height <= 1) return;
      using (GraphicsPath path = UiGeometry.Rounded(new Rectangle(0, 0, Width, Height), radius))
      {
        Region old = Region;
        Region = new Region(path);
        if (old != null) old.Dispose();
      }
    }

    protected override void OnPaintBackground(PaintEventArgs args)
    {
      PaintParentBackground(args);
      args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
      using (GraphicsPath path = UiGeometry.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), radius))
      using (var brush = new SolidBrush(BackColor)) args.Graphics.FillPath(brush, path);
    }

    private void PaintParentBackground(PaintEventArgs args)
    {
      int offsetX;
      int offsetY;
      Control ancestor = UiGeometry.BackgroundAncestor(this, out offsetX, out offsetY);
      if (ancestor == null)
      {
        args.Graphics.Clear(SystemColors.Control);
        return;
      }
      GraphicsState state = args.Graphics.Save();
      try
      {
        args.Graphics.TranslateTransform(-offsetX, -offsetY);
        using (var parentArgs = new PaintEventArgs(args.Graphics, new Rectangle(offsetX, offsetY, Width, Height)))
          InvokePaintBackground(ancestor, parentArgs);
      }
      finally { args.Graphics.Restore(state); }
    }

    protected override void OnPaint(PaintEventArgs args)
    {
      base.OnPaint(args);
      args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
      using (GraphicsPath path = UiGeometry.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), radius))
      using (var pen = new Pen(borderColor)) args.Graphics.DrawPath(pen, path);
    }
  }

  internal sealed class BrandMark : Control
  {
    private Image brandImage;

    public BrandMark(Icon icon)
    {
      SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
        ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
      BackColor = Color.Transparent;
      if (icon != null) brandImage = icon.ToBitmap();
      AccessibleName = "Codex Dream Skin";
      AccessibleRole = AccessibleRole.Graphic;
    }

    protected override void OnPaint(PaintEventArgs args)
    {
      args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
      Rectangle shadow = new Rectangle(3, 5, Width - 7, Height - 7);
      using (GraphicsPath path = UiGeometry.Rounded(shadow, 15))
      using (var brush = new SolidBrush(Color.FromArgb(32, 91, 61, 57))) args.Graphics.FillPath(brush, path);
      Rectangle surface = new Rectangle(2, 2, Width - 7, Height - 7);
      using (GraphicsPath path = UiGeometry.Rounded(surface, 15))
      using (var gradient = new LinearGradientBrush(surface, Color.FromArgb(255, 255, 255),
        Color.FromArgb(246, 230, 222), LinearGradientMode.ForwardDiagonal))
      {
        args.Graphics.FillPath(gradient, path);
        using (var pen = new Pen(Color.FromArgb(225, 201, 190))) args.Graphics.DrawPath(pen, path);
      }
      if (brandImage != null)
      {
        Rectangle target = new Rectangle(surface.Left + 10, surface.Top + 10, surface.Width - 20, surface.Height - 20);
        args.Graphics.DrawImage(brandImage, target);
      }
    }

    protected override void Dispose(bool disposing)
    {
      if (disposing && brandImage != null) { brandImage.Dispose(); brandImage = null; }
      base.Dispose(disposing);
    }
  }

  internal sealed class PillLabel : Label
  {
    private Color fillColor = Color.FromArgb(61, 132, 137);
    private Color textColor = Color.White;

    public PillLabel()
    {
      SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
        ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
      AutoSize = false;
      BackColor = Color.Transparent;
      Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
      TextAlign = ContentAlignment.MiddleCenter;
    }

    public Color FillColor
    {
      get { return fillColor; }
      set { fillColor = value; Invalidate(); }
    }

    public Color TextColor
    {
      get { return textColor; }
      set { textColor = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs args)
    {
      args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
      Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
      using (GraphicsPath path = UiGeometry.Rounded(bounds, Height / 2))
      using (var brush = new SolidBrush(fillColor)) args.Graphics.FillPath(brush, path);
      TextRenderer.DrawText(args.Graphics, Text, Font, bounds, textColor,
        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
  }

  internal sealed class DreamToggle : CheckBox
  {
    private bool hovered;
    private bool pressed;

    public DreamToggle()
    {
      SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
        ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
      Appearance = Appearance.Button;
      AutoSize = false;
      BackColor = Color.Transparent;
      Cursor = Cursors.Hand;
      Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
      Text = "开";
      TextAlign = ContentAlignment.MiddleCenter;
      AccessibleRole = AccessibleRole.CheckButton;
    }

    protected override void OnMouseEnter(EventArgs eventArgs) { hovered = true; Invalidate(); base.OnMouseEnter(eventArgs); }
    protected override void OnMouseLeave(EventArgs eventArgs) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(eventArgs); }
    protected override void OnMouseDown(MouseEventArgs eventArgs) { pressed = true; Invalidate(); base.OnMouseDown(eventArgs); }
    protected override void OnMouseUp(MouseEventArgs eventArgs) { pressed = false; Invalidate(); base.OnMouseUp(eventArgs); }
    protected override void OnGotFocus(EventArgs eventArgs) { Invalidate(); base.OnGotFocus(eventArgs); }
    protected override void OnLostFocus(EventArgs eventArgs) { Invalidate(); base.OnLostFocus(eventArgs); }
    protected override void OnEnabledChanged(EventArgs eventArgs) { Invalidate(); base.OnEnabledChanged(eventArgs); }
    protected override void OnCheckedChanged(EventArgs eventArgs)
    {
      Text = Checked ? "开" : "关";
      Invalidate();
      base.OnCheckedChanged(eventArgs);
    }

    protected override void OnPaintBackground(PaintEventArgs args)
    {
      int offsetX;
      int offsetY;
      Control ancestor = UiGeometry.BackgroundAncestor(this, out offsetX, out offsetY);
      if (ancestor == null)
      {
        args.Graphics.Clear(SystemColors.Control);
        return;
      }
      GraphicsState state = args.Graphics.Save();
      try
      {
        args.Graphics.TranslateTransform(-offsetX, -offsetY);
        using (var parentArgs = new PaintEventArgs(args.Graphics, new Rectangle(offsetX, offsetY, Width, Height)))
          InvokePaintBackground(ancestor, parentArgs);
      }
      finally { args.Graphics.Restore(state); }
    }

    protected override void OnPaint(PaintEventArgs args)
    {
      OnPaintBackground(args);
      args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
      Rectangle track = new Rectangle(1, 1, Width - 3, Height - 3);
      Color fill;
      Color border;
      Color knob;
      Color text;
      if (!Enabled)
      {
        fill = Color.FromArgb(236, 232, 228);
        border = Color.FromArgb(220, 212, 207);
        knob = Color.FromArgb(250, 248, 245);
        text = Color.FromArgb(150, 142, 140);
      }
      else if (Checked)
      {
        fill = pressed ? Color.FromArgb(47, 113, 118) : hovered ? Color.FromArgb(72, 147, 152) : Color.FromArgb(61, 132, 137);
        border = fill;
        knob = Color.White;
        text = Color.White;
      }
      else
      {
        fill = pressed ? Color.FromArgb(238, 224, 218) : hovered ? Color.FromArgb(249, 235, 229) : Color.FromArgb(255, 253, 249);
        border = Color.FromArgb(217, 188, 176);
        knob = Color.FromArgb(206, 103, 82);
        text = Color.FromArgb(174, 73, 59);
      }

      using (GraphicsPath path = UiGeometry.Rounded(track, track.Height / 2))
      using (var brush = new SolidBrush(fill))
      using (var pen = new Pen(border))
      {
        args.Graphics.FillPath(brush, path);
        args.Graphics.DrawPath(pen, path);
      }

      int knobSize = Math.Max(18, Height - 12);
      int knobLeft = Checked ? Width - knobSize - 7 : 6;
      Rectangle knobBounds = new Rectangle(knobLeft, (Height - knobSize) / 2, knobSize, knobSize);
      using (GraphicsPath path = UiGeometry.Rounded(knobBounds, knobSize / 2))
      using (var brush = new SolidBrush(knob)) args.Graphics.FillPath(brush, path);

      Rectangle textBounds = Checked
        ? new Rectangle(7, 0, Width - knobSize - 12, Height)
        : new Rectangle(knobBounds.Right + 2, 0, Width - knobBounds.Right - 5, Height);
      TextRenderer.DrawText(args.Graphics, Text, Font, textBounds, text,
        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
      if (Focused && ShowFocusCues)
      {
        Rectangle focusBounds = Rectangle.Inflate(track, -3, -3);
        using (GraphicsPath path = UiGeometry.Rounded(focusBounds, focusBounds.Height / 2))
        using (var pen = new Pen(Checked ? Color.White : Color.FromArgb(61, 132, 137)))
          args.Graphics.DrawPath(pen, path);
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
      BackColor = Color.Transparent;
      AccessibleRole = AccessibleRole.PushButton;
      TabStop = true;
    }

    public bool Primary
    {
      get { return primary; }
      set { primary = value; Invalidate(); }
    }

    public bool Danger
    {
      get { return danger; }
      set { danger = value; Invalidate(); }
    }

    protected override void OnMouseEnter(EventArgs eventArgs) { hovered = true; Invalidate(); base.OnMouseEnter(eventArgs); }
    protected override void OnMouseLeave(EventArgs eventArgs) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(eventArgs); }
    protected override void OnMouseDown(MouseEventArgs eventArgs) { pressed = true; Invalidate(); base.OnMouseDown(eventArgs); }
    protected override void OnMouseUp(MouseEventArgs eventArgs) { pressed = false; Invalidate(); base.OnMouseUp(eventArgs); }
    protected override void OnGotFocus(EventArgs eventArgs) { Invalidate(); base.OnGotFocus(eventArgs); }
    protected override void OnLostFocus(EventArgs eventArgs) { Invalidate(); base.OnLostFocus(eventArgs); }
    protected override void OnEnabledChanged(EventArgs eventArgs) { Invalidate(); base.OnEnabledChanged(eventArgs); }

    protected override void OnPaintBackground(PaintEventArgs args)
    {
      int offsetX;
      int offsetY;
      Control ancestor = UiGeometry.BackgroundAncestor(this, out offsetX, out offsetY);
      if (ancestor == null)
      {
        args.Graphics.Clear(SystemColors.Control);
        return;
      }
      GraphicsState state = args.Graphics.Save();
      try
      {
        args.Graphics.TranslateTransform(-offsetX, -offsetY);
        using (var parentArgs = new PaintEventArgs(args.Graphics, new Rectangle(offsetX, offsetY, Width, Height)))
          InvokePaintBackground(ancestor, parentArgs);
      }
      finally { args.Graphics.Restore(state); }
    }

    protected override void OnPaint(PaintEventArgs args)
    {
      OnPaintBackground(args);
      args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
      Rectangle buttonBounds = new Rectangle(1, 1, Width - 3, Height - 3);
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
        fill = pressed ? Color.FromArgb(164, 67, 54) : hovered ? Color.FromArgb(188, 82, 65) : Color.FromArgb(206, 103, 82);
        border = fill;
        text = Color.White;
      }
      else if (danger)
      {
        fill = pressed ? Color.FromArgb(244, 218, 215) : hovered ? Color.FromArgb(251, 235, 232) : Color.FromArgb(255, 253, 249);
        border = Color.FromArgb(218, 156, 149);
        text = Color.FromArgb(176, 61, 61);
      }
      else
      {
        fill = pressed ? Color.FromArgb(238, 228, 222) : hovered ? Color.FromArgb(248, 238, 232) : Color.FromArgb(255, 253, 249);
        border = hovered ? Color.FromArgb(211, 174, 159) : Color.FromArgb(226, 213, 205);
        text = Color.FromArgb(60, 45, 48);
      }

      using (GraphicsPath path = UiGeometry.Rounded(buttonBounds, 11))
      using (var brush = new SolidBrush(fill))
      using (var pen = new Pen(border))
      {
        args.Graphics.FillPath(brush, path);
        args.Graphics.DrawPath(pen, path);
      }
      TextRenderer.DrawText(args.Graphics, Text, Font, buttonBounds, text,
        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
      if (Focused && ShowFocusCues)
      {
        Rectangle focusBounds = Rectangle.Inflate(buttonBounds, -3, -3);
        using (GraphicsPath path = UiGeometry.Rounded(focusBounds, 8))
        using (var pen = new Pen(primary ? Color.White : Color.FromArgb(61, 132, 137)))
          args.Graphics.DrawPath(pen, path);
      }
    }
  }

  internal sealed class PreviewHost : RoundedPanel
  {
    public PreviewHost()
    {
      Radius = 18;
      BorderColor = Color.FromArgb(215, 198, 189);
    }
  }

  internal sealed class SkinListBox : ListBox
  {
    private readonly Dictionary<string, Image> thumbnails = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
    private readonly Font titleFont = new Font("Segoe UI", 10F, FontStyle.Bold);
    private readonly Font detailFont = new Font("Segoe UI", 8.5F, FontStyle.Regular);
    private readonly Font badgeFont = new Font("Segoe UI", 7.5F, FontStyle.Bold);
    private int hoveredIndex = -1;

    public SkinListBox()
    {
      DrawMode = DrawMode.OwnerDrawFixed;
      ItemHeight = 92;
      SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseMove(MouseEventArgs eventArgs)
    {
      base.OnMouseMove(eventArgs);
      int next = IndexFromPoint(eventArgs.Location);
      if (next == hoveredIndex) return;
      int previous = hoveredIndex;
      hoveredIndex = next;
      if (previous >= 0 && previous < Items.Count) Invalidate(GetItemRectangle(previous));
      if (hoveredIndex >= 0 && hoveredIndex < Items.Count) Invalidate(GetItemRectangle(hoveredIndex));
    }

    protected override void OnMouseLeave(EventArgs eventArgs)
    {
      base.OnMouseLeave(eventArgs);
      int previous = hoveredIndex;
      hoveredIndex = -1;
      if (previous >= 0 && previous < Items.Count) Invalidate(GetItemRectangle(previous));
    }

    protected override void OnDrawItem(DrawItemEventArgs args)
    {
      if (args.Index < 0 || args.Index >= Items.Count) return;
      SkinRecord skin = Items[args.Index] as SkinRecord;
      Graphics graphics = args.Graphics;
      graphics.SmoothingMode = SmoothingMode.AntiAlias;
      using (var clear = new SolidBrush(BackColor)) graphics.FillRectangle(clear, args.Bounds);
      Rectangle bounds = new Rectangle(args.Bounds.X + 3, args.Bounds.Y + 4, args.Bounds.Width - 7, args.Bounds.Height - 8);
      bool selected = (args.State & DrawItemState.Selected) == DrawItemState.Selected;
      bool hovered = args.Index == hoveredIndex;
      Rectangle shadow = new Rectangle(bounds.X + 1, bounds.Y + 2, bounds.Width, bounds.Height);
      using (GraphicsPath path = UiGeometry.Rounded(shadow, 12))
      using (var brush = new SolidBrush(Color.FromArgb(selected ? 28 : 14, 83, 57, 53))) graphics.FillPath(brush, path);
      Color background = selected ? Color.FromArgb(255, 248, 244) : hovered ? Color.FromArgb(255, 252, 248) : Color.FromArgb(252, 249, 245);
      Color outline = selected ? Color.FromArgb(218, 151, 133) : hovered ? Color.FromArgb(223, 203, 193) : Color.FromArgb(235, 226, 220);
      using (GraphicsPath path = UiGeometry.Rounded(bounds, 12))
      using (var brush = new SolidBrush(background))
      using (var pen = new Pen(outline))
      {
        graphics.FillPath(brush, path);
        graphics.DrawPath(pen, path);
      }
      if (selected)
      {
        Rectangle accent = new Rectangle(bounds.Left, bounds.Top + 15, 4, bounds.Height - 30);
        using (GraphicsPath path = UiGeometry.Rounded(accent, 2))
        using (var brush = new SolidBrush(Color.FromArgb(206, 103, 82))) graphics.FillPath(brush, path);
      }

      Rectangle imageBounds = new Rectangle(bounds.Left + 14, bounds.Top + 13, 58, 58);
      Image image = GetThumbnail(skin == null ? null : skin.PreviewPath);
      if (image != null)
      {
        GraphicsState state = graphics.Save();
        using (GraphicsPath path = UiGeometry.Rounded(imageBounds, 9)) graphics.SetClip(path);
        DrawCover(graphics, image, imageBounds);
        graphics.Restore(state);
        using (GraphicsPath path = UiGeometry.Rounded(imageBounds, 9))
        using (var pen = new Pen(Color.FromArgb(210, 190, 181))) graphics.DrawPath(pen, path);
      }
      else
      {
        using (GraphicsPath path = UiGeometry.Rounded(imageBounds, 9))
        using (var brush = new SolidBrush(Color.FromArgb(220, 229, 226))) graphics.FillPath(brush, path);
      }

      if (skin != null)
      {
        int textLeft = imageBounds.Right + 14;
        int textWidth = bounds.Right - textLeft - 12 - (skin.IsActive ? 48 : 0);
        TextRenderer.DrawText(graphics, skin.Manifest.name, titleFont,
          new Rectangle(textLeft, bounds.Top + 16, Math.Max(20, textWidth), 24), Color.FromArgb(60, 45, 48),
          TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
        string detail = skin.Manifest.author + "  ·  v" + skin.Manifest.version;
        TextRenderer.DrawText(graphics, detail, detailFont,
          new Rectangle(textLeft, bounds.Top + 46, bounds.Right - textLeft - 12, 20), Color.FromArgb(113, 94, 97),
          TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
        if (skin.IsActive)
        {
          Rectangle active = new Rectangle(bounds.Right - 53, bounds.Top + 13, 41, 21);
          using (GraphicsPath path = UiGeometry.Rounded(active, 10))
          using (var brush = new SolidBrush(Color.FromArgb(61, 132, 137))) graphics.FillPath(brush, path);
          TextRenderer.DrawText(graphics, "使用中", badgeFont, active, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
      }
      if (selected && Focused)
      {
        Rectangle focus = Rectangle.Inflate(bounds, -3, -3);
        using (GraphicsPath path = UiGeometry.Rounded(focus, 9))
        using (var pen = new Pen(Color.FromArgb(61, 132, 137))) graphics.DrawPath(pen, path);
      }
    }

    private Image GetThumbnail(string path)
    {
      if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
      Image cached;
      if (thumbnails.TryGetValue(path, out cached)) return cached;
      try
      {
        using (Image source = Image.FromFile(path))
        {
          var thumbnail = new Bitmap(116, 116, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
          using (Graphics graphics = Graphics.FromImage(thumbnail))
          {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            DrawCover(graphics, source, new Rectangle(0, 0, thumbnail.Width, thumbnail.Height));
          }
          cached = thumbnail;
        }
        thumbnails[path] = cached;
        return cached;
      }
      catch { return null; }
    }

    private static void DrawCover(Graphics graphics, Image image, Rectangle bounds)
    {
      float scale = Math.Max((float)bounds.Width / image.Width, (float)bounds.Height / image.Height);
      int width = (int)Math.Ceiling(image.Width * scale);
      int height = (int)Math.Ceiling(image.Height * scale);
      Rectangle target = new Rectangle(bounds.Left + (bounds.Width - width) / 2, bounds.Top + (bounds.Height - height) / 2, width, height);
      graphics.DrawImage(image, target);
    }

    protected override void Dispose(bool disposing)
    {
      if (disposing)
      {
        foreach (Image image in thumbnails.Values) image.Dispose();
        thumbnails.Clear();
        titleFont.Dispose();
        detailFont.Dispose();
        badgeFont.Dispose();
      }
      base.Dispose(disposing);
    }
  }
}
