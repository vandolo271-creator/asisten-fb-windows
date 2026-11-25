using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AsistenFB;
internal static class Program
{
    [STAThread] static void Main(){ ApplicationConfiguration.Initialize(); Application.Run(new MainForm()); }
}
public class MainForm : Form
{
    readonly WebView2 web = new();
    readonly Label status = new();
    readonly TextBox address = new();
    readonly string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AsistenFB","WebView2Profile");
    public MainForm()
    {
        Text="Asisten FB - Facebook di dalam aplikasi"; Width=1280; Height=820; MinimumSize=new Size(900,600);
        BackColor=Color.FromArgb(8,11,11);
        var header=new Panel{Dock=DockStyle.Top,Height=76,BackColor=Color.FromArgb(8,11,11)};
        header.Controls.Add(new Label{Text="ASISTEN FB",ForeColor=Color.FromArgb(217,255,0),Font=new Font("Segoe UI",20,FontStyle.Bold),AutoSize=true,Location=new Point(16,7)});
        header.Controls.Add(new Label{Text="Facebook tampil di dalam jendela aplikasi",ForeColor=Color.Gainsboro,AutoSize=true,Location=new Point(19,45)});
        var bar=new Panel{Dock=DockStyle.Top,Height=48,BackColor=Color.FromArgb(25,35,35)};
        Button B(string t,int x,int w,Action a){var b=new Button{Text=t,Location=new Point(x,7),Size=new Size(w,32),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(217,255,0),ForeColor=Color.Black};b.FlatAppearance.BorderSize=0;b.Click+=(_,_)=>a();return b;}
        bar.Controls.Add(B("Kembali",8,78,()=>{if(web.CanGoBack)web.GoBack();}));
        bar.Controls.Add(B("Maju",92,68,()=>{if(web.CanGoForward)web.GoForward();}));
        bar.Controls.Add(B("Muat ulang",166,92,()=>web.Reload()));
        bar.Controls.Add(B("Beranda FB",264,100,()=>web.Source=new Uri("https://www.facebook.com/")));
        address.SetBounds(372,9,610,28); address.Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Top; address.Text="https://www.facebook.com/"; bar.Controls.Add(address);
        var go=B("Buka",990,70,()=>Navigate()); go.Anchor=AnchorStyles.Top|AnchorStyles.Right; bar.Controls.Add(go);
        status.Dock=DockStyle.Bottom;status.Height=27;status.BackColor=Color.FromArgb(17,25,25);status.ForeColor=Color.FromArgb(217,255,0);status.TextAlign=ContentAlignment.MiddleLeft;status.Padding=new Padding(8,0,0,0);
        web.Dock=DockStyle.Fill; Controls.Add(web);Controls.Add(status);Controls.Add(bar);Controls.Add(header);
        Shown+=async(_,_)=>await StartBrowser();
    }
    void Navigate(){var s=address.Text.Trim();if(!s.StartsWith("http"))s="https://"+s;if(Uri.TryCreate(s,UriKind.Absolute,out var u))web.Source=u;else MessageBox.Show("Alamat tidak valid.");}
    async System.Threading.Tasks.Task StartBrowser()
    {
        try{
            Directory.CreateDirectory(profile);
            var env=await CoreWebView2Environment.CreateAsync(null,profile);
            await web.EnsureCoreWebView2Async(env);
            web.CoreWebView2.Settings.AreDevToolsEnabled=false;
            web.NavigationStarting+=(_,e)=>{status.Text="Membuka halaman Facebook...";if(Uri.TryCreate(e.Uri,UriKind.Absolute,out var u))address.Text=u.ToString();};
            web.NavigationCompleted+=(_,e)=>status.Text=e.IsSuccess?"Halaman selesai dimuat.":"Halaman gagal dimuat; coba muat ulang.";
            web.Source=new Uri("https://www.facebook.com/");
            status.Text="Silakan login langsung di situs Facebook.";
        }catch(Exception ex){
            status.Text="WebView2 belum siap.";
            MessageBox.Show("WebView2 Runtime diperlukan. Pasang Evergreen Runtime resmi Microsoft:\nhttps://developer.microsoft.com/microsoft-edge/webview2/\n\n"+ex.Message,"WebView2",MessageBoxButtons.OK,MessageBoxIcon.Information);
        }
    }
}