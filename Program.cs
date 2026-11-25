using System;
using System.Drawing;
using System.IO;
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

public class MainForm : Form
{
    private readonly WebView2 web = new();
    private readonly Label browserStatus = new();
    private readonly TextBox address = new();
    private readonly Label queueStatus = new();
    private readonly Label countStatus = new();
    private readonly FlowLayoutPanel postList = new();
    private readonly Button runAllButton = new();
    private readonly Button scanButton = new();
    private readonly string profile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AsistenFB", "WebView2Profile");

    public MainForm()
    {
        Text = "Asisten FB - Facebook + Panel Asisten";
        Width = 1450;
        Height = 850;
        MinimumSize = new Size(1050, 650);
        BackColor = Color.FromArgb(8, 11, 11);

        var header = new Panel { Dock = DockStyle.Top, Height = 68, BackColor = Color.FromArgb(8, 11, 11) };
        header.Controls.Add(new Label {
            Text = "ASISTEN FB", ForeColor = Color.FromArgb(217, 255, 0),
            Font = new Font("Segoe UI", 19, FontStyle.Bold), AutoSize = true, Location = new Point(16, 5)
        });
        header.Controls.Add(new Label {
            Text = "Facebook di kiri • daftar kerja asisten di kanan",
            ForeColor = Color.Gainsboro, AutoSize = true, Location = new Point(19, 39)
        });

        var nav = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Color.FromArgb(25, 35, 35) };
        Button NavButton(string label, int x, int width, Action action)
        {
            var b = new Button { Text = label, Location = new Point(x, 6), Size = new Size(width, 31),
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(217, 255, 0), ForeColor = Color.Black };
            b.FlatAppearance.BorderSize = 0;
            b.Click += (_, _) => action();
            return b;
        }
        nav.Controls.Add(NavButton("Kembali", 8, 76, () => { if (web.CanGoBack) web.GoBack(); }));
        nav.Controls.Add(NavButton("Maju", 90, 64, () => { if (web.CanGoForward) web.GoForward(); }));
        nav.Controls.Add(NavButton("Muat ulang", 160, 90, () => web.Reload()));
        nav.Controls.Add(NavButton("Beranda FB", 256, 94, () => web.Source = new Uri("https://www.facebook.com/")));
        address.SetBounds(358, 8, 560, 27);
        address.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        address.Text = "https://www.facebook.com/";
        nav.Controls.Add(address);
        var go = NavButton("Buka", 926, 66, Navigate);
        go.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        nav.Controls.Add(go);

        // Configure the split only after WinForms has performed the initial layout.
        // Setting panel minimum sizes in the constructor can throw because the
        // SplitContainer initially has a very small width.
        var split = new SplitContainer {
            Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 5,
            BackColor = Color.FromArgb(8, 11, 11)
        };
        bool splitConfigured = false;

        web.Dock = DockStyle.Fill;
        split.Panel1.Controls.Add(web);

        BuildAssistantPanel(split.Panel2);

        browserStatus.Dock = DockStyle.Bottom;
        browserStatus.Height = 25;
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
            if (availableWidth >= 480 + 330)
            {
                split.Panel1MinSize = 480;
                split.Panel2MinSize = 330;
                split.SplitterDistance = Math.Min(900, availableWidth - split.Panel2MinSize);
                splitConfigured = true;
            }
            await StartBrowser();
        };
        Resize += (_, _) =>
        {
            if (!splitConfigured) return;
            int maxDistance = split.ClientSize.Width - split.SplitterWidth - split.Panel2MinSize;
            if (maxDistance >= split.Panel1MinSize && split.SplitterDistance > maxDistance)
                split.SplitterDistance = maxDistance;
        };
    }

    private void BuildAssistantPanel(Control host)
    {
        host.BackColor = Color.FromArgb(12, 17, 17);
        var title = new Label {
            Text = "DAFTAR POSTINGAN", Dock = DockStyle.Top, Height = 42,
            ForeColor = Color.FromArgb(217, 255, 0), Font = new Font("Segoe UI", 13, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter
        };

        var controls = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Color.FromArgb(18, 27, 27) };
        scanButton.Text = "SCAN BERANDA";
        scanButton.SetBounds(12, 10, 145, 34);
        scanButton.BackColor = Color.FromArgb(217, 255, 0);
        scanButton.ForeColor = Color.Black;
        scanButton.FlatStyle = FlatStyle.Flat;
        scanButton.FlatAppearance.BorderSize = 0;
        scanButton.Click += (_, _) => MessageBox.Show(
            "Tombol dan panel asisten sudah disiapkan. Namun Facebook tidak menyediakan API resmi untuk mengambil seluruh postingan beranda profil pribadi. Karena itu pemindaian belum dijalankan agar aplikasi tidak memakai scraping atau mengambil data dengan cara yang melanggar aturan.",
            "Batas akses Facebook", MessageBoxButtons.OK, MessageBoxIcon.Information);
        controls.Controls.Add(scanButton);

        countStatus.Text = "0 postingan • batas daftar 50 • usia maksimal 24 jam";
        countStatus.SetBounds(12, 51, 330, 25);
        countStatus.ForeColor = Color.Gainsboro;
        countStatus.Font = new Font("Segoe UI", 8.5f);
        controls.Controls.Add(countStatus);

        postList.Dock = DockStyle.Fill;
        postList.FlowDirection = FlowDirection.TopDown;
        postList.WrapContents = false;
        postList.AutoScroll = true;
        postList.Padding = new Padding(10);
        postList.BackColor = Color.FromArgb(12, 17, 17);

        var empty = new Panel { Width = 340, Height = 170, BackColor = Color.FromArgb(22, 31, 31), Margin = new Padding(0, 8, 0, 8) };
        empty.Controls.Add(new Label {
            Text = "BELUM ADA POSTINGAN", ForeColor = Color.FromArgb(217, 255, 0),
            Font = new Font("Segoe UI", 11, FontStyle.Bold), AutoSize = true, Location = new Point(18, 18)
        });
        empty.Controls.Add(new Label {
            Text = "Daftar ini akan menampilkan postingan yang berhasil diperoleh melalui akses resmi. Kami tidak mengisi contoh palsu atau meminta Anda mengetik tautan postingan satu per satu.",
            ForeColor = Color.Gainsboro, Font = new Font("Segoe UI", 9),
            Location = new Point(18, 54), Size = new Size(300, 90)
        });
        postList.Controls.Add(empty);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 144, BackColor = Color.FromArgb(18, 27, 27) };
        queueStatus.Text = "Status AI: menunggu data postingan";
        queueStatus.SetBounds(12, 10, 330, 24);
        queueStatus.ForeColor = Color.Gainsboro;
        footer.Controls.Add(queueStatus);

        runAllButton.Text = "JALANKAN SEMUA";
        runAllButton.SetBounds(12, 42, 330, 42);
        runAllButton.BackColor = Color.FromArgb(55, 65, 65);
        runAllButton.ForeColor = Color.White;
        runAllButton.FlatStyle = FlatStyle.Flat;
        runAllButton.FlatAppearance.BorderSize = 0;
        runAllButton.Enabled = false;
        runAllButton.Click += (_, _) => MessageBox.Show(
            "Belum ada postingan terpilih dengan analisis dan draf yang siap. Tombol ini akan aktif setelah data dan draf tersedia.",
            "Belum siap", MessageBoxButtons.OK, MessageBoxIcon.Information);
        footer.Controls.Add(runAllButton);

        footer.Controls.Add(new Label {
            Text = "Tombol SUKA dan KOMENTAR AI per postingan akan tersedia pada setiap kartu postingan yang valid.",
            ForeColor = Color.Silver, Font = new Font("Segoe UI", 8),
            Location = new Point(12, 92), Size = new Size(330, 42)
        });

        host.Controls.Add(postList);
        host.Controls.Add(footer);
        host.Controls.Add(controls);
        host.Controls.Add(title);
    }

    private void Navigate()
    {
        var value = address.Text.Trim();
        if (!value.StartsWith("http", StringComparison.OrdinalIgnoreCase)) value = "https://" + value;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)) web.Source = uri;
        else MessageBox.Show("Alamat tidak valid.");
    }

    private async System.Threading.Tasks.Task StartBrowser()
    {
        try
        {
            Directory.CreateDirectory(profile);
            var env = await CoreWebView2Environment.CreateAsync(null, profile);
            await web.EnsureCoreWebView2Async(env);
            web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            web.NavigationStarting += (_, e) => {
                browserStatus.Text = "Membuka halaman Facebook...";
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)) address.Text = uri.ToString();
            };
            web.NavigationCompleted += (_, e) => browserStatus.Text =
                e.IsSuccess ? "Facebook selesai dimuat. Silakan login di situs Facebook." : "Halaman gagal dimuat; coba muat ulang.";
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
