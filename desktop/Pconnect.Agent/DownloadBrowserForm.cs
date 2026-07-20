using System.Diagnostics;
using Pconnect.Agent.Services;

namespace Pconnect.Agent;

internal sealed class DownloadBrowserForm : Form
{
    private readonly FileTransferManager _fileTransfer = new();
    private readonly Stack<string> _pathHistory = new();
    private string _currentPath = string.Empty;

    private readonly Panel _topPanel = new();
    private readonly Button _btnBack = new();
    private readonly Label _lblBreadcrumb = new();
    private readonly ListView _listView = new();
    private readonly Button _btnOpenFolder = new();

    public DownloadBrowserForm()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Pconnect — Shared Files & Downloads";
        Size = new Size(680, 480);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(24, 24, 28);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

        InitializeFormComponents();
        LoadDirectory(string.Empty);
    }

    private void InitializeFormComponents()
    {
        _topPanel.Dock = DockStyle.Top;
        _topPanel.Height = 44;
        _topPanel.Padding = new Padding(8);
        _topPanel.BackColor = Color.FromArgb(32, 32, 38);

        _btnBack.Text = "← Back";
        _btnBack.Width = 70;
        _btnBack.Dock = DockStyle.Left;
        _btnBack.FlatStyle = FlatStyle.Flat;
        _btnBack.FlatAppearance.BorderSize = 0;
        _btnBack.BackColor = Color.FromArgb(48, 48, 56);
        _btnBack.ForeColor = Color.White;
        _btnBack.Click += (_, _) => NavigateBack();

        _lblBreadcrumb.Dock = DockStyle.Fill;
        _lblBreadcrumb.TextAlign = ContentAlignment.MiddleLeft;
        _lblBreadcrumb.Padding = new Padding(12, 0, 0, 0);
        _lblBreadcrumb.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);

        _topPanel.Controls.Add(_lblBreadcrumb);
        _topPanel.Controls.Add(_btnBack);

        _listView.Dock = DockStyle.Fill;
        _listView.View = View.Details;
        _listView.FullRowSelect = true;
        _listView.BackColor = Color.FromArgb(24, 24, 28);
        _listView.ForeColor = Color.White;
        _listView.BorderStyle = BorderStyle.None;

        _listView.Columns.Add("Name", 320);
        _listView.Columns.Add("Type", 100);
        _listView.Columns.Add("Size", 120);

        _listView.DoubleClick += OnItemDoubleClick;

        _btnOpenFolder.Text = "Open in File Explorer";
        _btnOpenFolder.Dock = DockStyle.Bottom;
        _btnOpenFolder.Height = 40;
        _btnOpenFolder.FlatStyle = FlatStyle.Flat;
        _btnOpenFolder.FlatAppearance.BorderSize = 0;
        _btnOpenFolder.BackColor = Color.FromArgb(40, 40, 48);
        _btnOpenFolder.ForeColor = Color.White;
        _btnOpenFolder.Click += (_, _) => OpenCurrentInExplorer();

        Controls.Add(_listView);
        Controls.Add(_topPanel);
        Controls.Add(_btnOpenFolder);
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
                    Tag = new { path = itemPath, isDir }
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
        var tag = (dynamic)_listView.SelectedItems[0].Tag;
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
