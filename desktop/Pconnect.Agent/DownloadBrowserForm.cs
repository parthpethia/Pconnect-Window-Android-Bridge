using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Pconnect.Agent.Services;
using Pconnect.Agent.UI;

namespace Pconnect.Agent;

internal sealed class DownloadBrowserForm : Form
{
    private readonly FileTransferManager _fileTransfer = new();
    private readonly Stack<string> _pathHistory = new();
    private string _currentPath = string.Empty;

    private readonly Panel _topPanel = new();
    private readonly ModernButton _btnBack = new();
    private readonly Label _lblBreadcrumb = new();
    private readonly ListView _listView = new();
    private readonly ModernButton _btnOpenFolder = new();

    public DownloadBrowserForm()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Pconnect — Shared Files & Downloads";
        Size = new Size(700, 500);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = ThemeColors.Background;
        ForeColor = ThemeColors.TextPrimary;
        Font = ThemeColors.BodyFont;

        InitializeFormComponents();
        LoadDirectory(string.Empty);
    }

    private void InitializeFormComponents()
    {
        _topPanel.Dock = DockStyle.Top;
        _topPanel.Height = 48;
        _topPanel.Padding = new Padding(10, 6, 10, 6);
        _topPanel.BackColor = ThemeColors.Surface;

        _btnBack.Text = "← Back";
        _btnBack.Width = 85;
        _btnBack.Height = 34;
        _btnBack.Style = ModernButtonStyle.Secondary;
        _btnBack.Click += (_, _) => NavigateBack();

        _lblBreadcrumb.Dock = DockStyle.Fill;
        _lblBreadcrumb.TextAlign = ContentAlignment.MiddleLeft;
        _lblBreadcrumb.Padding = new Padding(12, 0, 0, 0);
        _lblBreadcrumb.Font = ThemeColors.BoldBodyFont;
        _lblBreadcrumb.ForeColor = ThemeColors.TextPrimary;

        _topPanel.Controls.Add(_lblBreadcrumb);
        _topPanel.Controls.Add(_btnBack);

        _listView.Dock = DockStyle.Fill;
        _listView.View = View.Details;
        _listView.FullRowSelect = true;
        _listView.BackColor = ThemeColors.Background;
        _listView.ForeColor = ThemeColors.TextPrimary;
        _listView.BorderStyle = BorderStyle.None;

        _listView.Columns.Add("Name", 340);
        _listView.Columns.Add("Type", 110);
        _listView.Columns.Add("Size", 130);

        _listView.DoubleClick += OnItemDoubleClick;

        var bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            Padding = new Padding(12, 8, 12, 8),
            BackColor = ThemeColors.Surface,
        };

        _btnOpenFolder.Text = "Open Folder in File Explorer";
        _btnOpenFolder.Dock = DockStyle.Fill;
        _btnOpenFolder.Style = ModernButtonStyle.Primary;
        _btnOpenFolder.Click += (_, _) => OpenCurrentInExplorer();

        bottomPanel.Controls.Add(_btnOpenFolder);

        Controls.Add(_listView);
        Controls.Add(_topPanel);
        Controls.Add(bottomPanel);
    }

    private void LoadDirectory(string path)
    {
        _currentPath = path;
        _lblBreadcrumb.Text = string.IsNullOrEmpty(path) ? "Roots: Desktop, Documents, Downloads" : path;
        _btnBack.Enabled = !string.IsNullOrEmpty(path);

        _listView.Items.Clear();

        var itemsObj = _fileTransfer.ListAllowedDirectory(path);
        if (itemsObj is System.Collections.IEnumerable list)
        {
            foreach (dynamic item in list)
            {
                string name = item.name;
                string itemPath = item.path;
                bool isDir = item.isDir;
                long size = item.size;

                var lvi = new ListViewItem(name)
                {
                    Tag = new { path = itemPath, isDir },
                    ForeColor = ThemeColors.TextPrimary,
                };

                lvi.SubItems.Add(isDir ? "Folder" : "File");
                lvi.SubItems.Add(isDir ? "" : FormatSize(size));

                _listView.Items.Add(lvi);
            }
        }
    }

    private void OnItemDoubleClick(object? sender, EventArgs e)
    {
        if (_listView.SelectedItems.Count == 0) return;
        var tagObj = _listView.SelectedItems[0].Tag;
        if (tagObj is null) return;
        var tag = (dynamic)tagObj;
        string path = tag.path;
        bool isDir = tag.isDir;

        if (isDir)
        {
            _pathHistory.Push(_currentPath);
            LoadDirectory(path);
        }
        else if (File.Exists(path))
        {
            try
            {
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            catch { }
        }
    }

    private void NavigateBack()
    {
        if (_pathHistory.Count > 0)
        {
            var prev = _pathHistory.Pop();
            LoadDirectory(prev);
        }
        else
        {
            LoadDirectory(string.Empty);
        }
    }

    private void OpenCurrentInExplorer()
    {
        var target = string.IsNullOrEmpty(_currentPath) ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : _currentPath;
        try
        {
            Process.Start("explorer.exe", target);
        }
        catch { }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{(bytes / 1024.0):F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{(bytes / (1024.0 * 1024.0)):F1} MB";
        return $"{(bytes / (1024.0 * 1024.0 * 1024.0)):F2} GB";
    }
}

