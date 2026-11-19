using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

// Sets the Large Address Aware flag on a 32-bit PE exe. Same logic as laa-patch.ps1.
static class Pe
{
    public class Info { public byte[] Bytes; public int ChrOff, SumOff; public bool Laa { get { return (BitConverter.ToUInt16(Bytes, ChrOff) & 0x20) != 0; } }
        public ushort Chr { get { return BitConverter.ToUInt16(Bytes, ChrOff); } }
        public uint Sum { get { return BitConverter.ToUInt32(Bytes, SumOff); } } }

    public static byte[] ReadShared(string p)
    {
        using (var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        { var b = new byte[fs.Length]; int n = 0; while (n < b.Length) { int r = fs.Read(b, n, b.Length - n); if (r <= 0) break; n += r; } return b; }
    }

    // Throws InvalidDataException with a readable reason if not a 32-bit PE32.
    public static Info Parse(byte[] b)
    {
        if (b.Length < 0x40 || b[0] != 0x4D || b[1] != 0x5A) throw new InvalidDataException("Not an MZ executable.");
        int pe = BitConverter.ToInt32(b, 0x3C);
        if (pe < 0x40 || pe + 0x58 > b.Length) throw new InvalidDataException("PE header offset out of range.");
        if (BitConverter.ToUInt32(b, pe) != 0x4550) throw new InvalidDataException("PE signature missing.");
        if (BitConverter.ToUInt16(b, pe + 4) != 0x14C) throw new InvalidDataException("Not an i386 (32-bit) image.");
        if (BitConverter.ToUInt16(b, pe + 24) != 0x10B) throw new InvalidDataException("Not PE32 (optional header magic 0x10B).");
        return new Info { Bytes = b, ChrOff = pe + 22, SumOff = pe + 24 + 64 };
    }

    // Standard PE checksum: 16-bit word sum with carry fold, checksum field skipped, plus file length.
    public static uint Checksum(byte[] b, int skip)
    {
        ulong s = 0;
        for (int i = 0; i < b.Length; i += 2)
        {
            if (i == skip || i == skip + 2) continue;
            s += i + 1 < b.Length ? (uint)(b[i] | (b[i + 1] << 8)) : (uint)b[i];
            if (s > 0xFFFFFFFF) s = (s & 0xFFFFFFFF) + (s >> 32);
        }
        s = (s & 0xFFFF) + (s >> 16); s = (s & 0xFFFF) + (s >> 16);
        return (uint)(s + (ulong)b.Length);
    }

    public static void SetLaa(Info i)
    {
        BitConverter.GetBytes((ushort)(i.Chr | 0x20)).CopyTo(i.Bytes, i.ChrOff);
        BitConverter.GetBytes(Checksum(i.Bytes, i.SumOff)).CopyTo(i.Bytes, i.SumOff);
    }

    // Exclusive open: throws IOException while the game runs.
    public static void WriteExclusive(string p, byte[] b)
    {
        using (var fs = new FileStream(p, FileMode.Create, FileAccess.Write, FileShare.None)) fs.Write(b, 0, b.Length);
    }

    public static bool Same(byte[] a, byte[] b) { return a.Length == b.Length && a.SequenceEqual(b); }

    public static string FindSsfiv()
    {
        try
        {
            string steam = null;
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")) if (k != null) steam = k.GetValue("SteamPath") as string;
            if (string.IsNullOrEmpty(steam)) return null;
            steam = steam.Replace('/', '\\');
            var libs = new List<string> { steam };
            string vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    libs.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
            foreach (var lib in libs)
            {
                string apps = Path.Combine(lib, "steamapps"), dir = "Super Street Fighter IV - Arcade Edition";
                string acf = Path.Combine(apps, "appmanifest_45760.acf");
                if (File.Exists(acf)) { var m = Regex.Match(File.ReadAllText(acf), "\"installdir\"\\s+\"([^\"]+)\""); if (m.Success) dir = m.Groups[1].Value; }
                string exe = Path.Combine(apps, "common", dir, "SSFIV.exe");
                if (File.Exists(exe)) return exe;
            }
        }
        catch { }
        return null;
    }
}

class MainForm : Form
{
    TextBox path = new TextBox();
    Label status = new Label(), detail = new Label();
    Button browse = new Button(), patch = new Button(), restore = new Button(), copy = new Button();

    public MainForm(string initial)
    {
        Text = "LAA Patch - Large Address Aware for 32-bit games";
        AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(640, 430); MinimumSize = new Size(560, 440);
        StartPosition = FormStartPosition.CenterScreen;

        var lp = new Label { Text = "&Game exe:", Left = 12, Top = 16, AutoSize = true };
        path.SetBounds(12, 40, 520, 27); path.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; path.TabIndex = 1;
        lp.TabIndex = 0;
        browse.Text = "&Browse..."; browse.SetBounds(542, 38, 86, 31); browse.Anchor = AnchorStyles.Top | AnchorStyles.Right; browse.TabIndex = 2;
        status.Name = "status"; detail.Name = "detail"; path.Name = "path"; status.SetBounds(12, 84, 616, 40); status.Font = new Font("Segoe UI", 18f, FontStyle.Bold); status.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        detail.SetBounds(12, 126, 616, 82); detail.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        patch.Text = "&Patch SSFIV.exe for Ember (keeps original as SSFIV.exe.bak)"; patch.SetBounds(12, 214, 616, 44); patch.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; patch.Name = "patch"; patch.TabIndex = 3;
        restore.Text = "&Restore original SSFIV.exe"; restore.SetBounds(12, 268, 230, 40); restore.Name = "restore"; restore.TabIndex = 4;
        copy.Text = "Patch a &copy instead"; copy.SetBounds(252, 268, 190, 40); copy.Name = "copy"; copy.TabIndex = 5;
        var note = new Label { Left = 12, Top = 324, Width = 616, Height = 90, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Text = "Gives the game 4 GB of address space on 64-bit Windows. It does not make it 64-bit. Steam 'Verify integrity' undoes it. Close the game first; keep the .bak backup." };
        Controls.AddRange(new Control[] { lp, path, browse, status, detail, patch, restore, copy, note });
        AcceptButton = patch;

        browse.Click += (s, e) => { using (var d = new OpenFileDialog { Filter = "Executables (*.exe)|*.exe", FileName = path.Text }) if (d.ShowDialog(this) == DialogResult.OK) path.Text = d.FileName; };
        path.TextChanged += (s, e) => { done = ""; Refresh2(); };
        patch.Click += (s, e) => Run(false);
        copy.Click += (s, e) => Run(true);
        restore.Click += (s, e) => DoRestore();
        path.TextChanged += (s, e) => touched = true;   // typing or Browse cancels the auto-fill
        if (!string.IsNullOrEmpty(initial)) { path.Text = initial; return; }
        Show("Looking for SSFIV.exe...", "Searching Steam libraries. You can already click Browse.", Color.Gray);
        patch.Enabled = restore.Enabled = copy.Enabled = false;
        var t = new System.Threading.Thread(() =>
        {
            string f = Pe.FindSsfiv();
            try { BeginInvoke((Action)(() => { if (touched) return; if (f != null) path.Text = f; else Refresh2(); })); } catch { }
        }) { IsBackground = true };
        HandleCreated += (s, e) => t.Start();   // BeginInvoke needs the handle
    }

    bool touched; string done = "";
    string Bak { get { return path.Text + ".bak"; } }

    void Refresh2()
    {
        patch.Enabled = restore.Enabled = copy.Enabled = false;
        string p = path.Text.Trim('"', ' ');
        if (!File.Exists(p)) { Show("Choose the game exe", "No file selected (or it does not exist). Click Browse.", Color.Gray); return; }
        try
        {
            var i = Pe.Parse(Pe.ReadShared(p));
            bool bak = File.Exists(p + ".bak");
            Show("Large Address Aware: " + (i.Laa ? "ON" : "OFF"),
                "32-bit PE32 (i386): OK.   Backup (.bak) present: " + (bak ? "yes" : "no") + "\nCharacteristics 0x" + i.Chr.ToString("X") + ", checksum 0x" + i.Sum.ToString("X8") + done,
                i.Laa ? Color.DarkGreen : Color.Firebrick);
            patch.Enabled = copy.Enabled = !i.Laa; restore.Enabled = bak;
        }
        catch (Exception ex) { Show("Cannot use this file", ex.Message, Color.Firebrick); }
    }

    void Show(string s, string d, Color c) { status.Text = s; status.ForeColor = c; detail.Text = d; }

    void Run(bool asCopy)
    {
        done = "";
        string p = path.Text.Trim('"', ' ');
        try
        {
            var i = Pe.Parse(Pe.ReadShared(p));
            if (i.Laa) { Refresh2(); return; }
            byte[] orig = (byte[])i.Bytes.Clone();
            if (!asCopy && File.Exists(p + ".bak") && !Pe.Same(File.ReadAllBytes(p + ".bak"), orig))
            { Msg(p + ".bak already exists and differs from the current unpatched file. Move or delete it yourself, then retry."); return; }
            Pe.SetLaa(i);
            if (asCopy) { string o = Path.ChangeExtension(p, ".laa.exe"); Pe.WriteExclusive(o, i.Bytes); done = "\nWrote " + o + " (Ember still starts SSFIV.exe)"; }
            else
            {
                using (var fs = new FileStream(p, FileMode.Open, FileAccess.ReadWrite, FileShare.None))   // exclusive: fails if in use
                {
                    if (!File.Exists(p + ".bak")) File.WriteAllBytes(p + ".bak", orig);
                    fs.Write(i.Bytes, 0, i.Bytes.Length);
                }
                done = "\nEmber will now start the 4 GB version. Steam 'Verify integrity' or a game update restores the original; run this again then.";
            }
        }
        catch (Exception ex) { Fail(ex, p); }
        Refresh2();
    }

    void DoRestore()
    {
        done = "";
        string p = path.Text.Trim('"', ' ');
        try
        {
            var bak = Pe.Parse(File.ReadAllBytes(p + ".bak"));
            if (bak.Laa) { Msg(p + ".bak is itself patched, so it is not the original. Not restoring."); return; }
            var cur = Pe.Parse(Pe.ReadShared(p));
            if (!cur.Laa) done = "\nSSFIV.exe is already unpatched; backup left as is.";
            else
            {
                // the patched exe must equal the backup apart from the LAA bit and the checksum
                var x = (byte[])cur.Bytes.Clone(); var y = (byte[])bak.Bytes.Clone();
                foreach (var q in new[] { new { b = x, o = cur }, new { b = y, o = bak } })
                { q.b[q.o.ChrOff] &= 0xDF; Array.Clear(q.b, q.o.SumOff, 4); }
                if (!Pe.Same(x, y)) { Msg("SSFIV.exe differs from the backup by more than the LAA flag (game updated?). Not restoring."); return; }
                Pe.WriteExclusive(p, bak.Bytes);
                done = "\nOriginal restored; Ember starts the 2 GB version again.";
            }
        }
        catch (Exception ex) { Fail(ex, p); }
        Refresh2();
    }

    void Fail(Exception ex, string p)
    {
        if (ex is UnauthorizedAccessException)
        {
            if (MessageBox.Show(this, "Windows denied write access to this folder.\nRestart LAA Patch as administrator?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                try { Process.Start(new ProcessStartInfo(Application.ExecutablePath, "\"" + p + "\"") { UseShellExecute = true, Verb = "runas" }); Close(); }
                catch { }
            }
        }
        else if (ex is IOException) Msg("The file is in use (is the game running?). Close it and try again.");
        else Msg(ex.Message);
    }

    void Msg(string m) { MessageBox.Show(this, m, Text); }
}

static class Program
{
    [DllImport("kernel32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--selftest") return SelfTest();
        try { SetProcessDPIAware(); } catch { }
        Application.EnableVisualStyles();
        Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
        return 0;
    }

    static StringBuilder log = new StringBuilder(); static int fails;
    static void Check(bool ok, string m) { log.AppendLine((ok ? "ok: " : "FAIL: ") + m); if (!ok) fails++; }

    static int SelfTest()
    {
        string d = Path.Combine(Path.GetTempPath(), "laa-app-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d);
        try
        {
            var b = new byte[0x201]; new Random(7).NextBytes(b);
            b[0] = 0x4D; b[1] = 0x5A; BitConverter.GetBytes(0x80).CopyTo(b, 0x3C);
            BitConverter.GetBytes(0x4550u).CopyTo(b, 0x80); BitConverter.GetBytes((ushort)0x14C).CopyTo(b, 0x84);
            BitConverter.GetBytes((ushort)0x103).CopyTo(b, 0x96); BitConverter.GetBytes((ushort)0x10B).CopyTo(b, 0x98);
            var i = Pe.Parse((byte[])b.Clone());
            Check(!i.Laa, "synthetic: flag off initially");
            Pe.SetLaa(i);
            Check(i.Laa && i.Chr == 0x123, "synthetic: flag set");
            Check(i.Sum == Pe.Checksum(i.Bytes, i.SumOff), "synthetic: checksum matches recompute");
            byte[] once = (byte[])i.Bytes.Clone(); Pe.SetLaa(i);
            Check(Pe.Same(once, i.Bytes), "synthetic: second run is a no-op");
            // restore round trip through files
            string f = Path.Combine(d, "a.exe"); File.WriteAllBytes(f, b); File.Copy(f, f + ".bak");
            Pe.WriteExclusive(f, i.Bytes); Pe.WriteExclusive(f, File.ReadAllBytes(f + ".bak"));
            Check(Pe.Same(File.ReadAllBytes(f), b), "synthetic: restore exact");
            bool th = false; try { Pe.Parse(new byte[100]); } catch (InvalidDataException) { th = true; }
            Check(th, "non-PE refused");
            using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read))
            { bool inuse = false; try { Pe.WriteExclusive(f, b); } catch (IOException) { inuse = true; } Check(inuse, "file in use refused"); }

            var sw = Stopwatch.StartNew(); string src = Pe.FindSsfiv();
            log.AppendLine("FindSsfiv took " + sw.ElapsedMilliseconds + " ms");
            if (src == null) log.AppendLine("SSFIV not found: real-file comparison skipped");
            else
            {
                string refp = Path.Combine(Path.GetDirectoryName(src), "SSFIV.ember-laa.exe");
                var r = Pe.Parse(Pe.ReadShared(src)); Pe.SetLaa(r);
                var k = Pe.ReadShared(refp);
                Check(r.Bytes.Length == k.Length, "SSFIV: same length as known-good");
                var diff = new List<string>();
                for (int n = 0; n < k.Length && n < r.Bytes.Length; n++) if (r.Bytes[n] != k[n]) diff.Add(n >= r.SumOff && n < r.SumOff + 4 ? "CheckSum" : "0x" + n.ToString("X"));
                var u = diff.Distinct().ToList();
                Check(u.Count == 0 || (u.Count == 1 && u[0] == "CheckSum"), "SSFIV vs known-good: " + diff.Count + " differing bytes: " + string.Join(",", u.ToArray()));
                log.AppendLine("checksum ours 0x" + r.Sum.ToString("X8") + " known-good 0x" + BitConverter.ToUInt32(k, r.SumOff).ToString("X8"));
            }
        }
        finally { Directory.Delete(d, true); }
        log.AppendLine(fails == 0 ? "ALL PASS" : fails + " FAILED");
        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest.txt"), log.ToString());
        return fails;
    }
}
