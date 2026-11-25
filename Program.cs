using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AsistenFB;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

public sealed class SavedPost
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public DateTime AddedAt { get; set; } = DateTime.Now;
    public bool Selected { get; set; }
}

public class MainForm : Form
{
    private readonly WebView2 web = new();
    private readonly Label browserStatus = new();
    private readonly TextBox address = new();
    private readonly Label countStatus = new();
    private readonly Label infoStatus = new();
    private readonly FlowLayoutPanel postList = new();
    private readonly Button captureButton = new();
    private readonly Button clearButton = new();
    private readonly Button deleteSelectedButton = new();
    private readonly List<SavedPost> posts = new();
    private readonly string profile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AsistenFB", "WebView2Profile");
    private readonly string dataFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AsistenFB", "reading-list.json");

    public MainForm()
    {
        Text = "Asisten FB - Daftar Bacaan";
        Width = 1500;
        Height = 900;
        MinimumSize = new Size(1050, 650);
        BackColor = Color.FromArgb(8, 11, 11);

        LoadPosts();

        var header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Color.FromArgb(8, 11, 11) };
        header.Controls.Add(new Label {
            Text = "ASISTEN FB", ForeColor = Color.FromArgb(217, 255, 0),
            Font = new Font("Segoe UI", 19, FontStyle.Bold), AutoSize = true, Location = new Point(16, 3)
        });
        header.Controls.Add(new Label {
            Text = "Pilih teks postingan di Facebook kiri, lalu masukkan ke daftar bacaan kanan",
            ForeColor = Color.Gainsboro, AutoSize = true, Location = new Point(19, 38)
        });

        var nav = new Panel { Dock = DockStyle.Top, Height = 43, BackColor = Color.FromArgb(25, 35, 35) };
        Button NavButton(string label, int x, int width, Action action)
        {
            var b = new Button { Text = label, Location = new Point(x, 6), Size = new Size(width, 30),
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(217, 255, 0), ForeColor = Color.Black };
            b.FlatAppearance.BorderSize = 0;
            b.Click += (_, _) => action();
            return b;
        }
        nav.Controls.Add(NavButton("Kembali", 8, 76, () => { if (web.CanGoBack) web.GoBack(); }));
        nav.Controls.Add(NavButton("Maju", 90, 64, () => { if (web.CanGoForward) web.GoForward(); }));
        nav.Controls.Add(NavButton("Muat ulang", 160, 90, () => web.Reload()));
        nav.Controls.Add(NavButton("Beranda FB", 256, 94, () => web.Source = new Uri("https://www.facebook.com/")));
        address.SetBounds(358, 8, 560, 26);
        address.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        address.Text = "https://www.facebook.com/";
        nav.Controls.Add(address);
        var go = NavButton("Buka", 926, 66, Navigate);
        go.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        nav.Controls.Add(go);

        var split = new SplitContainer {
            Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 6,
            BackColor = Color.FromArgb(8, 11, 11)
        };
        bool splitConfigured = false;
        web.Dock = DockStyle.Fill;
        split.Panel1.Controls.Add(web);
        BuildReadingPanel(split.Panel2);

        browserStatus.Dock = DockStyle.Bottom;
        browserStatus.Height = 24;
        browserStatus.BackColor = Color.FromArgb(17, 25, 25);
        browserStatus.ForeColor = Color.FromArgb(217, 255, 0);
        browserStatus.TextAlign = ContentAlignment.MiddleLeft;
        browserStatus.Padding = new Padding(8, 0, 0, 0);

        Controls.Add(split);
        Controls.Add(browserStatus);
        Controls.Add(nav);
        Controls.Add(header);

        Shown += async (_, _) =>
        {
            int availableWidth = split.ClientSize.Width - split.SplitterWidth;
            if (availableWidth >= 480 + 380)
            {
                split.Panel1MinSize = 480;
                split.Panel2MinSize = 380;
                split.SplitterDistance = Math.Min(850, availableWidth - split.Panel2MinSize);
                splitConfigured = true;
            }
            await StartBrowser();
            RefreshPostCards();
        };
        Resize += (_, _) =>
        {
            if (!splitConfigured) return;
            int maxDistance = split.ClientSize.Width - split.SplitterWidth - split.Panel2MinSize;
            if (maxDistance >= split.Panel1MinSize && split.SplitterDistance > maxDistance)
                split.SplitterDistance = maxDistance;
        };
    }

    private void BuildReadingPanel(Control host)
    {
        host.BackColor = Color.FromArgb(12, 17, 17);
        var title = new Label {
            Text = "DAFTAR BACAAN", Dock = DockStyle.Top, Height = 42,
            ForeColor = Color.FromArgb(217, 255, 0), Font = new Font("Segoe UI", 13, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter
        };

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 142, BackColor = Color.FromArgb(18, 27, 27) };
        captureButton.Text = "＋ TAMBAHKAN TEKS TERPILIH";
        captureButton.SetBounds(10, 8, 350, 36);
        captureButton.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        captureButton.BackColor = Color.FromArgb(217, 255, 0);
        captureButton.ForeColor = Color.Black;
        captureButton.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        captureButton.FlatStyle = FlatStyle.Flat;
        captureButton.FlatAppearance.BorderSize = 0;
        captureButton.Click += async (_, _) => await CaptureSelection();
        toolbar.Controls.Add(captureButton);

        countStatus.SetBounds(12, 49, 350, 20);
        countStatus.ForeColor = Color.Gainsboro;
        countStatus.Font = new Font("Segoe UI", 8.5f);
        toolbar.Controls.Add(countStatus);

        var instruction = new Label {
            Text = "Cara: blok teks postingan di Facebook sebelah kiri, lalu klik tombol hijau. Item tersimpan di komputer ini.",
            ForeColor = Color.Silver, Font = new Font("Segoe UI", 8.5f),
            Location = new Point(12, 72), Size = new Size(350, 42), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        toolbar.Controls.Add(instruction);

        postList.Dock = DockStyle.Fill;
        postList.FlowDirection = FlowDirection.TopDown;
        postList.WrapContents = false;
        postList.AutoScroll = true;
        postList.Padding = new Padding(8);
        postList.BackColor = Color.FromArgb(12, 17, 17);
        postList.SizeChanged += (_, _) => RefreshPostCards();

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 92, BackColor = Color.FromArgb(18, 27, 27) };
        deleteSelectedButton.Text = "HAPUS YANG DICENTANG";
        deleteSelectedButton.SetBounds(10, 8, 210, 34);
        deleteSelectedButton.BackColor = Color.FromArgb(55, 65, 65);
        deleteSelectedButton.ForeColor = Color.White;
        deleteSelectedButton.FlatStyle = FlatStyle.Flat;
        deleteSelectedButton.FlatAppearance.BorderSize = 0;
        deleteSelectedButton.Click += (_, _) => DeleteSelected();
        footer.Controls.Add(deleteSelectedButton);

        clearButton.Text = "KOSONGKAN DAFTAR";
        clearButton.SetBounds(228, 8, 132, 34);
        clearButton.BackColor = Color.FromArgb(90, 35, 35);
        clearButton.ForeColor = Color.White;
        clearButton.FlatStyle = FlatStyle.Flat;
        clearButton.FlatAppearance.BorderSize = 0;
        clearButton.Click += (_, _) => ClearAll();
        footer.Controls.Add(clearButton);

        infoStatus.Text = "Penyimpanan lokal • maksimal 50 item";
        infoStatus.SetBounds(12, 49, 350, 22);
        infoStatus.ForeColor = Color.Silver;
        infoStatus.Font = new Font("Segoe UI", 8);
        footer.Controls.Add(infoStatus);

        host.Controls.Add(postList);
        host.Controls.Add(footer);
        host.Controls.Add(toolbar);
        host.Controls.Add(title);
    }

    private async Task CaptureSelection()
    {
        if (web.CoreWebView2 == null)
        {
            MessageBox.Show("Halaman Facebook belum siap.", "Asisten FB");
            return;
        }

        try
        {
            string json = await web.CoreWebView2.ExecuteScriptAsync("window.getSelection ? window.getSelection().toString() : '';");
            string selectedText = JsonSerializer.Deserialize<string>(json) ?? "";
            selectedText = selectedText.Trim();

            if (selectedText.Length < 8)
            {
                MessageBox.Show("Blok atau sorot dahulu teks postingan di Facebook sebelah kiri, kemudian klik tombol hijau.", "Belum ada teks yang dipilih");
                return;
            }

            if (selectedText.Length > 5000) selectedText = selectedText[..5000];
            string source = web.Source?.ToString() ?? "https://www.facebook.com/";
            bool duplicate = posts.Any(p => string.Equals(Normalize(p.Text), Normalize(selectedText), StringComparison.OrdinalIgnoreCase));
            if (duplicate)
            {
                MessageBox.Show("Teks postingan ini sudah ada di daftar.", "Duplikat");
                return;
            }
            if (posts.Count >= 50)
            {
                MessageBox.Show("Daftar sudah mencapai batas 50 item. Hapus beberapa item terlebih dahulu.", "Daftar penuh");
                return;
            }

            posts.Insert(0, new SavedPost { Text = selectedText, SourceUrl = source, AddedAt = DateTime.Now });
            SavePosts();
            RefreshPostCards();
            infoStatus.Text = "Postingan berhasil ditambahkan ke daftar.";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Tidak dapat mengambil teks pilihan: " + ex.Message, "Gagal menambahkan");
        }
    }

    private static string Normalize(string value) =>
        string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private void RefreshPostCards()
    {
        if (postList.IsDisposed) return;
        postList.SuspendLayout();
        postList.Controls.Clear();
        int cardWidth = Math.Max(280, postList.ClientSize.Width - 38);

        foreach (var post in posts.ToList())
        {
            var card = new Panel {
                Width = cardWidth, Height = 164, BackColor = Color.FromArgb(23, 32, 32),
                Margin = new Padding(2, 4, 2, 5), Padding = new Padding(8), BorderStyle = BorderStyle.FixedSingle
            };
            var check = new CheckBox {
                Text = "Pilih", Checked = post.Selected, AutoSize = true,
                ForeColor = Color.FromArgb(217, 255, 0), Location = new Point(8, 7)
            };
            check.CheckedChanged += (_, _) => { post.Selected = check.Checked; SavePosts(); };
            card.Controls.Add(check);

            var when = new Label {
                Text = "Ditambahkan: " + post.AddedAt.ToString("dd/MM HH:mm"),
                ForeColor = Color.Silver, AutoSize = true, Location = new Point(82, 9)
            };
            card.Controls.Add(when);

            var excerpt = new Label {
                Text = post.Text.Length > 250 ? post.Text[..250] + "…" : post.Text,
                ForeColor = Color.White, Font = new Font("Segoe UI", 8.5f),
                Location = new Point(8, 34), Size = new Size(cardWidth - 24, 78),
                AutoEllipsis = true
            };
            card.Controls.Add(excerpt);

            var open = new Button {
                Text = "BUKA SUMBER", Location = new Point(8, 119), Size = new Size(112, 28),
                BackColor = Color.FromArgb(217, 255, 0), ForeColor = Color.Black, FlatStyle = FlatStyle.Flat
            };
            open.FlatAppearance.BorderSize = 0;
            open.Click += (_, _) => OpenSource(post.SourceUrl);
            card.Controls.Add(open);

            var remove = new Button {
                Text = "HAPUS", Location = new Point(128, 119), Size = new Size(72, 28),
                BackColor = Color.FromArgb(75, 42, 42), ForeColor = Color.White, FlatStyle = FlatStyle.Flat
            };
            remove.FlatAppearance.BorderSize = 0;
            remove.Click += (_, _) => { posts.Remove(post); SavePosts(); RefreshPostCards(); };
            card.Controls.Add(remove);

            postList.Controls.Add(card);
        }

        countStatus.Text = $"{posts.Count} / 50 item tersimpan";
        if (posts.Count == 0)
        {
            var empty = new Label {
                Text = "DAFTAR MASIH KOSONG\n\nSorot teks postingan di panel Facebook, lalu tekan tombol hijau di atas.",
                ForeColor = Color.Gainsboro, Font = new Font("Segoe UI", 10),
                TextAlign = ContentAlignment.MiddleCenter, Width = cardWidth, Height = 130
            };
            postList.Controls.Add(empty);
        }
        postList.ResumeLayout();
    }

    private void DeleteSelected()
    {
        int removed = posts.RemoveAll(p => p.Selected);
        SavePosts();
        RefreshPostCards();
        infoStatus.Text = removed + " item terpilih dihapus.";
    }

    private void ClearAll()
    {
        if (posts.Count == 0) return;
        if (MessageBox.Show("Hapus semua item dari daftar bacaan?", "Konfirmasi", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        posts.Clear();
        SavePosts();
        RefreshPostCards();
        infoStatus.Text = "Daftar bacaan dikosongkan.";
    }

    private void OpenSource(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) web.Source = uri;
        else MessageBox.Show("Alamat sumber tidak valid.");
    }

    private void LoadPosts()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dataFile)!);
            if (!File.Exists(dataFile)) return;
            var loaded = JsonSerializer.Deserialize<List<SavedPost>>(File.ReadAllText(dataFile));
            if (loaded != null) posts.AddRange(loaded.Take(50));
        }
        catch { /* Keep the app usable if the local list file is damaged. */ }
    }

    private void SavePosts()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dataFile)!);
            File.WriteAllText(dataFile, JsonSerializer.Serialize(posts, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            infoStatus.Text = "Gagal menyimpan daftar: " + ex.Message;
        }
    }

    private void Navigate()
    {
        var value = address.Text.Trim();
        if (!value.StartsWith("http", StringComparison.OrdinalIgnoreCase)) value = "https://" + value;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)) web.Source = uri;
        else MessageBox.Show("Alamat tidak valid.");
    }

    private async Task StartBrowser()
    {
        try
        {
            Directory.CreateDirectory(profile);
            var env = await CoreWebView2Environment.CreateAsync(null, profile);
            await web.EnsureCoreWebView2Async(env);
            web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            web.NavigationStarting += (_, e) =>
            {
                browserStatus.Text = "Membuka halaman Facebook...";
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)) address.Text = uri.ToString();
            };
            web.NavigationCompleted += (_, e) => browserStatus.Text =
                e.IsSuccess ? "Facebook siap. Login langsung di situs Facebook; sorot teks postingan untuk menyimpannya." : "Halaman gagal dimuat; coba muat ulang.";
            web.Source = new Uri("https://www.facebook.com/");
            browserStatus.Text = "Silakan login langsung di situs Facebook.";
        }
        catch (Exception ex)
        {
            browserStatus.Text = "WebView2 belum siap.";
            MessageBox.Show("WebView2 Runtime diperlukan. Pasang Evergreen Runtime resmi Microsoft:\nhttps://developer.microsoft.com/microsoft-edge/webview2/\n\n" + ex.Message,
                "WebView2", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
