using System;
using System.Drawing;
using System.Windows.Forms;

namespace CodexDreamSkinManager
{
  internal sealed class RenameSkinDialog : Form
  {
    private static readonly Color Canvas = Color.FromArgb(247, 244, 239);
    private static readonly Color Ink = Color.FromArgb(60, 45, 48);
    private static readonly Color Muted = Color.FromArgb(113, 94, 97);
    private static readonly Color Border = Color.FromArgb(226, 213, 205);
    private static readonly Color Danger = Color.FromArgb(176, 61, 61);

    private readonly TextBox nameTextBox;
    private readonly Label errorLabel;

    public RenameSkinDialog(string currentName)
    {
      Text = "重命名皮肤";
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
      ClientSize = new Size(460, 224);

      var title = new Label
      {
        AutoSize = true,
        Text = "重命名皮肤",
        Font = new Font("Segoe UI", 16F, FontStyle.Bold),
        ForeColor = Ink,
        Location = new Point(26, 20)
      };
      var fieldLabel = new Label
      {
        AutoSize = true,
        Text = "皮肤名称",
        Font = new Font("Segoe UI", 9F, FontStyle.Bold),
        ForeColor = Muted,
        Location = new Point(27, 65)
      };
      var inputHost = new RoundedPanel
      {
        Location = new Point(26, 88),
        Size = new Size(408, 46),
        Radius = 11,
        BorderColor = Border,
        BackColor = Color.FromArgb(255, 253, 249)
      };
      nameTextBox = new TextBox
      {
        BorderStyle = BorderStyle.None,
        BackColor = Color.FromArgb(255, 253, 249),
        ForeColor = Ink,
        Font = new Font("Segoe UI", 11F),
        Location = new Point(12, 12),
        Size = new Size(384, 24),
        MaxLength = 60,
        Text = currentName ?? string.Empty,
        AccessibleName = "皮肤名称"
      };
      inputHost.Controls.Add(nameTextBox);

      errorLabel = new Label
      {
        AutoSize = true,
        Text = "请输入皮肤名称。",
        Font = new Font("Segoe UI", 8.5F),
        ForeColor = Danger,
        Location = new Point(28, 141),
        Visible = false
      };
      nameTextBox.TextChanged += delegate { errorLabel.Visible = false; };

      var cancelButton = new DreamButton
      {
        Text = "取消",
        Size = new Size(82, 42),
        Location = new Point(258, 166),
        Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
        Cursor = Cursors.Hand,
        AccessibleName = "取消"
      };
      cancelButton.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
      var confirmButton = new DreamButton
      {
        Text = "保存",
        Size = new Size(88, 42),
        Location = new Point(346, 166),
        Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
        Cursor = Cursors.Hand,
        AccessibleName = "保存皮肤名称",
        Primary = true
      };
      confirmButton.Click += delegate
      {
        if (string.IsNullOrWhiteSpace(nameTextBox.Text))
        {
          errorLabel.Visible = true;
          nameTextBox.Focus();
          return;
        }
        DialogResult = DialogResult.OK;
        Close();
      };

      Controls.Add(title);
      Controls.Add(fieldLabel);
      Controls.Add(inputHost);
      Controls.Add(errorLabel);
      Controls.Add(cancelButton);
      Controls.Add(confirmButton);
      AcceptButton = confirmButton;
      CancelButton = cancelButton;
      Shown += delegate
      {
        nameTextBox.Focus();
        nameTextBox.SelectAll();
      };
    }

    public string SkinName
    {
      get { return nameTextBox.Text.Trim(); }
    }
  }
}
