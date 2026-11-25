import tkinter as tk
from tkinter import ttk, messagebox, filedialog
import webbrowser, subprocess, os, json
from datetime import datetime

APP_DIR = os.path.dirname(os.path.abspath(__file__))
DATA_DIR = os.path.join(APP_DIR, "data")
QUEUE_FILE = os.path.join(DATA_DIR, "posts.json")
os.makedirs(DATA_DIR, exist_ok=True)

BG = "#080b0b"
PANEL = "#111919"
LIME = "#D9FF00"
CYAN = "#04FFFF"
WHITE = "#F2F5F5"
MUTED = "#9AA7A7"

def load_items():
    try:
        with open(QUEUE_FILE, "r", encoding="utf-8") as f:
            return json.load(f)
    except Exception:
        return []

def save_items():
    with open(QUEUE_FILE, "w", encoding="utf-8") as f:
        json.dump(items, f, ensure_ascii=False, indent=2)

def open_edge(url):
    # Use an ordinary Edge window and its existing browser profile.
    candidates = [
        os.path.join(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)"),
                     "Microsoft", "Edge", "Application", "msedge.exe"),
        os.path.join(os.environ.get("ProgramFiles", r"C:\Program Files"),
                     "Microsoft", "Edge", "Application", "msedge.exe"),
        "msedge"
    ]
    for exe in candidates:
        try:
            subprocess.Popen([exe, url], shell=False)
            return
        except Exception:
            pass
    webbrowser.open(url)

def login_fb():
    open_edge("https://www.facebook.com/login/")
    messagebox.showinfo("Login Facebook",
        "Facebook dibuka di Microsoft Edge. Silakan login langsung di situs Facebook.\n\n"
        "Asisten FB tidak meminta, membaca, atau menyimpan kata sandi maupun cookie Anda.")

def open_feed():
    open_edge("https://www.facebook.com/")

def add_links():
    raw = link_text.get("1.0", "end").strip()
    if not raw:
        messagebox.showwarning("Belum ada tautan", "Tempel satu atau beberapa tautan postingan terlebih dahulu.")
        return
    added = 0
    for line in raw.splitlines():
        url = line.strip()
        if url.startswith("https://") and ("facebook.com" in url or "fb.watch" in url):
            if not any(x.get("url") == url for x in items):
                items.append({"url": url, "status": "Menunggu", "comment": ""})
                added += 1
    save_items()
    refresh()
    link_text.delete("1.0", "end")
    messagebox.showinfo("Antrean", f"{added} tautan ditambahkan.")

def selected_index():
    sel = tree.selection()
    if not sel:
        return None
    return int(sel[0])

def refresh():
    for row in tree.get_children():
        tree.delete(row)
    for i, item in enumerate(items):
        tree.insert("", "end", iid=str(i), values=(i+1, item.get("status","Menunggu"), item.get("url","")))

def open_selected():
    i = selected_index()
    if i is None:
        messagebox.showwarning("Pilih postingan", "Pilih satu tautan dari antrean.")
        return
    open_edge(items[i]["url"])

def copy_comment():
    i = selected_index()
    if i is None:
        messagebox.showwarning("Pilih postingan", "Pilih satu tautan dari antrean.")
        return
    comment = items[i].get("comment", "").strip()
    if not comment:
        messagebox.showinfo("Komentar", "Isi kolom komentar draf terlebih dahulu.")
        return
    root.clipboard_clear()
    root.clipboard_append(comment)
    root.update()
    messagebox.showinfo("Disalin", "Draf komentar disalin. Anda dapat menempelkannya sendiri di Facebook.")

def save_comment():
    i = selected_index()
    if i is None:
        messagebox.showwarning("Pilih postingan", "Pilih satu tautan dari antrean.")
        return
    items[i]["comment"] = comment_text.get("1.0", "end").strip()
    save_items()
    messagebox.showinfo("Tersimpan", "Draf komentar disimpan.")

def mark_done():
    i = selected_index()
    if i is None:
        messagebox.showwarning("Pilih postingan", "Pilih satu tautan dari antrean.")
        return
    items[i]["status"] = "Selesai manual"
    save_items()
    refresh()

def delete_selected():
    i = selected_index()
    if i is None:
        messagebox.showwarning("Pilih postingan", "Pilih satu tautan dari antrean.")
        return
    if messagebox.askyesno("Hapus", "Hapus tautan terpilih dari antrean?"):
        items.pop(i)
        save_items()
        refresh()

def import_txt():
    path = filedialog.askopenfilename(filetypes=[("Text files","*.txt"),("All files","*.*")])
    if not path:
        return
    with open(path, "r", encoding="utf-8") as f:
        link_text.insert("end", f.read())
    add_links()

def export_txt():
    path = filedialog.asksaveasfilename(defaultextension=".txt", filetypes=[("Text files","*.txt")])
    if not path:
        return
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(x.get("url","") for x in items))
    messagebox.showinfo("Ekspor", "Daftar tautan berhasil disimpan.")

items = load_items()
root = tk.Tk()
root.title("Asisten FB - Edge")
root.geometry("980x720")
root.minsize(780, 600)
root.configure(bg=BG)

style = ttk.Style()
style.theme_use("clam")
style.configure("Treeview", background=PANEL, foreground=WHITE, fieldbackground=PANEL, rowheight=28)
style.configure("Treeview.Heading", background="#263333", foreground=LIME, font=("Segoe UI", 9, "bold"))
style.map("Treeview", background=[("selected", "#344000")])

def heading(parent, text):
    tk.Label(parent, text=text, bg=parent["bg"], fg=LIME, font=("Segoe UI", 11, "bold")).pack(anchor="w", pady=(8,5))

top = tk.Frame(root, bg=BG, padx=18, pady=14)
top.pack(fill="x")
tk.Label(top, text="ASISTEN FB", bg=BG, fg=LIME, font=("Segoe UI", 22, "bold")).pack(anchor="w")
tk.Label(top, text="Pendamping antrean tautan Facebook • Windows 10/11", bg=BG, fg=MUTED, font=("Segoe UI", 9)).pack(anchor="w")

login_panel = tk.Frame(root, bg=PANEL, padx=14, pady=12)
login_panel.pack(fill="x", padx=18, pady=6)
tk.Label(login_panel, text="AKSES FACEBOOK MELALUI MICROSOFT EDGE", bg=PANEL, fg=CYAN, font=("Segoe UI", 10, "bold")).pack(anchor="w", pady=(0,8))
btns = tk.Frame(login_panel, bg=PANEL)
btns.pack(anchor="w")
tk.Button(btns, text="LOGIN FACEBOOK", command=login_fb, bg=LIME, fg="#101400", relief="flat", padx=16, pady=8, font=("Segoe UI",9,"bold")).pack(side="left", padx=(0,8))
tk.Button(btns, text="BUKA BERANDA", command=open_feed, bg="#263333", fg=WHITE, relief="flat", padx=16, pady=8).pack(side="left")
tk.Label(login_panel, text="Login berlangsung di Edge. Jangan pernah memasukkan kata sandi atau cookie Facebook ke aplikasi ini.", bg=PANEL, fg=MUTED, font=("Segoe UI",8)).pack(anchor="w", pady=(8,0))

main = tk.Frame(root, bg=BG, padx=18)
main.pack(fill="both", expand=True)
heading(main, "1. TAMBAHKAN TAUTAN POSTINGAN")
link_text = tk.Text(main, height=4, bg="#101616", fg=WHITE, insertbackground=LIME, relief="flat", font=("Consolas",9), wrap="word")
link_text.pack(fill="x")
actions = tk.Frame(main, bg=BG)
actions.pack(fill="x", pady=7)
for label, cmd in [("Tambah ke antrean", add_links), ("Impor TXT", import_txt), ("Ekspor TXT", export_txt)]:
    tk.Button(actions, text=label, command=cmd, bg="#263333", fg=WHITE, relief="flat", padx=12, pady=6).pack(side="left", padx=(0,7))

heading(main, "2. ANTREAN TAUTAN")
tree = ttk.Treeview(main, columns=("no","status","url"), show="headings", height=9)
tree.heading("no", text="No.")
tree.heading("status", text="Status")
tree.heading("url", text="Tautan postingan")
tree.column("no", width=55, anchor="center")
tree.column("status", width=140, anchor="center")
tree.column("url", width=650)
tree.pack(fill="both", expand=True)
row_actions = tk.Frame(main, bg=BG)
row_actions.pack(fill="x", pady=7)
for label, cmd in [("Buka postingan", open_selected), ("Tandai selesai", mark_done), ("Hapus", delete_selected)]:
    tk.Button(row_actions, text=label, command=cmd, bg="#263333", fg=WHITE, relief="flat", padx=12, pady=6).pack(side="left", padx=(0,7))

heading(main, "3. DRAF KOMENTAR (ANDA YANG MENEMPEL DAN MENGIRIM)")
comment_text = tk.Text(main, height=3, bg="#101616", fg=WHITE, insertbackground=LIME, relief="flat", font=("Segoe UI",9), wrap="word")
comment_text.pack(fill="x")
comment_actions = tk.Frame(main, bg=BG)
comment_actions.pack(fill="x", pady=7)
tk.Button(comment_actions, text="Simpan draf", command=save_comment, bg="#263333", fg=WHITE, relief="flat", padx=12, pady=6).pack(side="left", padx=(0,7))
tk.Button(comment_actions, text="Salin draf komentar", command=copy_comment, bg=LIME, fg="#101400", relief="flat", padx=12, pady=6).pack(side="left")

note = ("Catatan: aplikasi ini tidak membaca seluruh Beranda Facebook, tidak mengambil postingan secara diam-diam, "
        "dan tidak melakukan Like/komentar otomatis. Tautan dimasukkan oleh Anda; aplikasi membantu menyimpan antrean, "
        "membuka postingan di Edge, dan menyalin draf komentar.")
tk.Label(root, text=note, bg=BG, fg=MUTED, justify="left", wraplength=930, font=("Segoe UI",8), padx=18, pady=10).pack(fill="x")

refresh()
root.mainloop()
