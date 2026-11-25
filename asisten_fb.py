import json
import os
import re
import sys
import tkinter as tk
from tkinter import ttk, filedialog, messagebox
from pathlib import Path
from datetime import datetime, timezone
import webbrowser
import urllib.parse

APP_NAME = "Asisten FB"
CHECK_INTERVAL_MS = 30 * 60 * 1000
MIN_AGE_HOURS_DEFAULT = 24

def app_dir():
    # Keep data beside the EXE, not in PyInstaller's temporary extraction folder.
    if getattr(sys, "frozen", False):
        return Path(sys.executable).resolve().parent
    return Path(__file__).resolve().parent

ROOT = app_dir()
DATA_DIR = ROOT / "data"
DATA_DIR.mkdir(exist_ok=True)
POSTS_FILE = DATA_DIR / "posts.json"
SETTINGS_FILE = DATA_DIR / "settings.json"

def load_json(path, fallback):
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except Exception:
        return fallback

def save_json(path, value):
    path.parent.mkdir(exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")

def parse_date(value):
    value = (value or "").strip()
    if not value:
        return None
    # Accept ISO date/time, including "2026-11-24 15:30".
    try:
        dt = datetime.fromisoformat(value.replace("Z", "+00:00"))
        if dt.tzinfo is None:
            dt = dt.astimezone()
        return dt
    except Exception:
        return None

def age_hours(post):
    dt = parse_date(post.get("posted_at", ""))
    if not dt:
        return None
    return (datetime.now().astimezone() - dt.astimezone()).total_seconds() / 3600

def valid_facebook_url(url):
    try:
        p = urllib.parse.urlparse(url.strip())
        return p.scheme in ("http", "https") and (
            p.netloc.lower().endswith("facebook.com") or
            p.netloc.lower().endswith("fb.watch") or
            p.netloc.lower().endswith("fb.com")
        )
    except Exception:
        return False

class AsistenFB:
    def __init__(self, root):
        self.root = root
        self.root.title("Asisten FB — Antrean Postingan")
        self.root.geometry("1060x700")
        self.root.minsize(900, 580)
        self.root.configure(bg="#080b0b")
        self.settings = load_json(SETTINGS_FILE, {"min_age_hours": MIN_AGE_HOURS_DEFAULT})
        self.posts = load_json(POSTS_FILE, [])
        self.selected_id = None
        self.next_check = None
        self._style()
        self._build()
        self.refresh_table()
        self.scan_queue(show_message=False)
        self.schedule_next()

    def _style(self):
        style = ttk.Style()
        try:
            style.theme_use("clam")
        except Exception:
            pass
        style.configure("Treeview", background="#101515", foreground="#e9f2ef",
                        fieldbackground="#101515", rowheight=30, borderwidth=0)
        style.configure("Treeview.Heading", background="#1b2523", foreground="#d9ff00",
                        font=("Segoe UI", 9, "bold"))
        style.map("Treeview", background=[("selected", "#29403a")],
                  foreground=[("selected", "#ffffff")])
        style.configure("TCombobox", fieldbackground="#101515", background="#101515",
                        foreground="#e9f2ef")

    def _button(self, parent, label, command, accent=False):
        return tk.Button(parent, text=label, command=command,
                         bg="#d9ff00" if accent else "#1a2925",
                         fg="#08100b" if accent else "#e9f2ef",
                         activebackground="#bde000", activeforeground="#08100b",
                         relief="flat", bd=0, padx=13, pady=8,
                         font=("Segoe UI", 9, "bold"), cursor="hand2")

    def _build(self):
        header = tk.Frame(self.root, bg="#080b0b")
        header.pack(fill="x", padx=18, pady=(16, 8))
        tk.Label(header, text="ASISTEN FB", bg="#080b0b", fg="#d9ff00",
                 font=("Segoe UI", 20, "bold")).pack(side="left")
        tk.Label(header, text="ANTREAN POSTINGAN • PEMERIKSAAN 30 MENIT",
                 bg="#080b0b", fg="#9fb2aa", font=("Segoe UI", 9, "bold")).pack(side="left", padx=16, pady=(8,0))
        self.status_label = tk.Label(header, text="Siap", bg="#080b0b", fg="#00eaff",
                                     font=("Segoe UI", 9, "bold"))
        self.status_label.pack(side="right", pady=8)

        top = tk.Frame(self.root, bg="#101515", highlightbackground="#33423d", highlightthickness=1)
        top.pack(fill="x", padx=18, pady=8)
        tk.Label(top, text="Tambah banyak tautan sekaligus (satu tautan per baris)",
                 bg="#101515", fg="#e9f2ef", font=("Segoe UI", 10, "bold")).pack(anchor="w", padx=12, pady=(10,5))
        self.urls_text = tk.Text(top, height=4, bg="#080d0c", fg="#e9f2ef", insertbackground="#d9ff00",
                                 relief="flat", wrap="word", font=("Consolas", 9))
        self.urls_text.pack(fill="x", padx=12, pady=(0,8))
        actions = tk.Frame(top, bg="#101515")
        actions.pack(fill="x", padx=12, pady=(0,10))
        self._button(actions, "Tambah ke antrean", self.add_pasted_urls, True).pack(side="left")
        self._button(actions, "Impor TXT", self.import_txt).pack(side="left", padx=7)
        self._button(actions, "Ekspor antrean", self.export_txt).pack(side="left")
        self._button(actions, "Pengaturan", self.settings_dialog).pack(side="right")

        mid = tk.Frame(self.root, bg="#080b0b")
        mid.pack(fill="both", expand=True, padx=18, pady=8)
        columns = ("number", "kind", "age", "state", "url")
        self.tree = ttk.Treeview(mid, columns=columns, show="headings", selectmode="browse")
        headings = {"number":"No.", "kind":"Jenis", "age":"Umur postingan", "state":"Status", "url":"Tautan"}
        widths = {"number":48, "kind":85, "age":120, "state":180, "url":540}
        for c in columns:
            self.tree.heading(c, text=headings[c])
            self.tree.column(c, width=widths[c], anchor="w")
        self.tree.pack(side="left", fill="both", expand=True)
        scroll = ttk.Scrollbar(mid, orient="vertical", command=self.tree.yview)
        scroll.pack(side="right", fill="y")
        self.tree.configure(yscrollcommand=scroll.set)
        self.tree.bind("<<TreeviewSelect>>", self.on_select)

        bottom = tk.Frame(self.root, bg="#101515", highlightbackground="#33423d", highlightthickness=1)
        bottom.pack(fill="x", padx=18, pady=(0,10))
        left = tk.Frame(bottom, bg="#101515")
        left.pack(side="left", fill="both", expand=True, padx=12, pady=10)
        tk.Label(left, text="DRAF KOMENTAR", bg="#101515", fg="#d9ff00",
                 font=("Segoe UI", 9, "bold")).pack(anchor="w")
        self.comment_text = tk.Text(left, height=3, bg="#080d0c", fg="#e9f2ef", insertbackground="#d9ff00",
                                    relief="flat", wrap="word", font=("Segoe UI", 9))
        self.comment_text.pack(fill="x", pady=(5,0))
        right = tk.Frame(bottom, bg="#101515")
        right.pack(side="right", padx=12, pady=12)
        self._button(right, "Buka postingan", self.open_selected, True).pack(fill="x", pady=3)
        self._button(right, "Salin komentar", self.copy_comment).pack(fill="x", pady=3)
        self._button(right, "Tandai selesai", self.mark_done).pack(fill="x", pady=3)
        self._button(right, "Hapus dari antrean", self.delete_selected).pack(fill="x", pady=3)

        footer = tk.Frame(self.root, bg="#080b0b")
        footer.pack(fill="x", padx=18, pady=(0,12))
        self.footer_label = tk.Label(footer, text="", bg="#080b0b", fg="#91a49c", font=("Segoe UI", 8))
        self.footer_label.pack(side="left")
        self._button(footer, "Periksa sekarang", self.manual_scan).pack(side="right")

        note = ("Catatan: aplikasi ini tidak membaca Beranda pribadi Facebook dan tidak melakukan Like/komen otomatis. "
                "Anda memasukkan tautan yang memang dapat Anda akses; aplikasi memeriksa umur berdasarkan tanggal yang Anda isi.")
        tk.Label(self.root, text=note, bg="#080b0b", fg="#82948d",
                 font=("Segoe UI", 8), wraplength=1000, justify="left").pack(fill="x", padx=18, pady=(0,12))

    def persist(self):
        save_json(POSTS_FILE, self.posts)

    def add_pasted_urls(self):
        raw = self.urls_text.get("1.0", "end").strip()
        urls = [x.strip() for x in re.split(r"[\r\n\t ]+", raw) if x.strip()]
        self.add_urls(urls)
        self.urls_text.delete("1.0", "end")

    def import_txt(self):
        path = filedialog.askopenfilename(filetypes=[("Text files","*.txt"),("All files","*.*")])
        if not path:
            return
        try:
            raw = Path(path).read_text(encoding="utf-8-sig")
        except Exception as e:
            messagebox.showerror("Gagal membaca file", str(e))
            return
        urls = [x.strip() for x in re.split(r"[\r\n\t ]+", raw) if x.strip()]
        self.add_urls(urls)

    def add_urls(self, urls):
        existing = {p["url"] for p in self.posts}
        added, rejected = 0, 0
        for url in urls:
            if not valid_facebook_url(url) or url in existing:
                rejected += 1
                continue
            if len(self.posts) >= 50:
                break
            self.posts.append({
                "id": str(int(datetime.now().timestamp()*1000)) + str(added),
                "url": url, "kind": "Belum dipilih", "posted_at": "",
                "status": "Menunggu tanggal", "comment": ""
            })
            existing.add(url)
            added += 1
        self.persist()
        self.refresh_table()
        messagebox.showinfo("Antrean diperbarui", f"Ditambahkan: {added}\nDiabaikan/duplikat: {rejected}\nKapasitas maksimum: 50 postingan.")

    def export_txt(self):
        path = filedialog.asksaveasfilename(defaultextension=".txt", filetypes=[("Text files","*.txt")])
        if not path:
            return
        Path(path).write_text("\n".join(p["url"] for p in self.posts), encoding="utf-8")
        messagebox.showinfo("Selesai", "Daftar tautan berhasil diekspor.")

    def refresh_table(self):
        for item in self.tree.get_children():
            self.tree.delete(item)
        min_age = float(self.settings.get("min_age_hours", 24))
        for i, p in enumerate(self.posts, 1):
            age = age_hours(p)
            age_text = f"{age:.1f} jam" if age is not None else "Tanggal belum diisi"
            state = p.get("status", "Menunggu")
            if age is not None and age < min_age and state not in ("Selesai",):
                state = f"Belum {min_age:g} jam"
            self.tree.insert("", "end", iid=p["id"], values=(i, p.get("kind","-"), age_text, state, p["url"]))
        self.footer_label.config(text=f"{len(self.posts)}/50 postingan • Batas umur: {min_age:g} jam • Pemeriksaan otomatis: 30 menit")

    def on_select(self, _event=None):
        selected = self.tree.selection()
        if not selected:
            self.selected_id = None
            return
        self.selected_id = selected[0]
        post = next((p for p in self.posts if p["id"] == self.selected_id), None)
        self.comment_text.delete("1.0", "end")
        if post:
            self.comment_text.insert("1.0", post.get("comment", ""))

    def selected_post(self):
        if not self.selected_id:
            messagebox.showwarning("Pilih postingan", "Pilih satu postingan pada daftar terlebih dahulu.")
            return None
        return next((p for p in self.posts if p["id"] == self.selected_id), None)

    def open_selected(self):
        p = self.selected_post()
        if p:
            webbrowser.open(p["url"])

    def copy_comment(self):
        p = self.selected_post()
        if not p:
            return
        comment = self.comment_text.get("1.0", "end").strip()
        if not comment:
            messagebox.showinfo("Komentar kosong", "Isi draf komentar terlebih dahulu.")
            return
        p["comment"] = comment
        self.persist()
        self.root.clipboard_clear()
        self.root.clipboard_append(comment)
        self.root.update()
        messagebox.showinfo("Disalin", "Komentar disalin. Anda dapat menempelkannya sendiri di Facebook.")

    def mark_done(self):
        p = self.selected_post()
        if not p:
            return
        p["status"] = "Selesai (ditandai manual)"
        p["comment"] = self.comment_text.get("1.0", "end").strip()
        self.persist()
        self.refresh_table()

    def delete_selected(self):
        p = self.selected_post()
        if not p:
            return
        if messagebox.askyesno("Hapus", "Hapus postingan ini dari antrean?"):
            self.posts = [x for x in self.posts if x["id"] != p["id"]]
            self.selected_id = None
            self.persist()
            self.refresh_table()
            self.comment_text.delete("1.0", "end")

    def settings_dialog(self):
        win = tk.Toplevel(self.root)
        win.title("Pengaturan")
        win.configure(bg="#101515")
        win.resizable(False, False)
        tk.Label(win, text="Umur minimum postingan (jam)", bg="#101515", fg="#e9f2ef").pack(padx=18, pady=(16,5))
        val = tk.StringVar(value=str(self.settings.get("min_age_hours",24)))
        entry = tk.Entry(win, textvariable=val, bg="#080d0c", fg="#d9ff00", insertbackground="#d9ff00", relief="flat")
        entry.pack(fill="x", padx=18, pady=5)
        tk.Label(win, text="Interval pemantauan tetap 30 menit.", bg="#101515", fg="#9fb2aa").pack(padx=18, pady=5)
        def save():
            try:
                n = float(val.get())
                if n < 0 or n > 8760:
                    raise ValueError()
            except Exception:
                messagebox.showerror("Nilai tidak valid", "Masukkan angka jam yang benar.")
                return
            self.settings["min_age_hours"] = n
            save_json(SETTINGS_FILE, self.settings)
            self.refresh_table()
            win.destroy()
        self._button(win, "Simpan", save, True).pack(pady=14)

    def scan_queue(self, show_message=True):
        min_age = float(self.settings.get("min_age_hours", 24))
        eligible = 0
        for p in self.posts:
            age = age_hours(p)
            if age is not None and age >= min_age and p.get("status") != "Selesai (ditandai manual)":
                p["status"] = "Siap ditinjau"
                eligible += 1
            elif age is not None and age < min_age and p.get("status") != "Selesai (ditandai manual)":
                p["status"] = "Menunggu umur minimum"
        self.persist()
        self.refresh_table()
        now = datetime.now().astimezone().strftime("%d-%m-%Y %H:%M:%S")
        self.status_label.config(text=f"Terakhir diperiksa: {now}")
        if show_message:
            messagebox.showinfo("Pemeriksaan selesai", f"{eligible} postingan memenuhi batas umur.\nAplikasi tidak melakukan Like atau komentar otomatis.")

    def manual_scan(self):
        self.scan_queue(show_message=True)

    def schedule_next(self):
        self.root.after(CHECK_INTERVAL_MS, self._scheduled_scan)

    def _scheduled_scan(self):
        self.scan_queue(show_message=False)
        self.schedule_next()

if __name__ == "__main__":
    root = tk.Tk()
    app = AsistenFB(root)
    root.mainloop()
