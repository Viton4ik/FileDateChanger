using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Xml.Linq;

[assembly: System.Reflection.AssemblyTitle("File Date Changer")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace FileDateChanger
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    class MainForm : Form
    {
        // ---------- Language / Язык ----------
        //
        // The interface follows the Windows display language: Russian on a
        // Russian system, English everywhere else. Override it with the
        // FDC_LANG environment variable or a /ru or /en switch on the command
        // line. Declared before any field whose initialiser calls T(), because
        // static initialisers run in declaration order.

        static readonly bool RU = DetectRussian();

        static bool DetectRussian()
        {
            string forced = Environment.GetEnvironmentVariable("FDC_LANG");
            if (!string.IsNullOrEmpty(forced))
                return forced.Trim().ToLowerInvariant().StartsWith("ru");

            foreach (string arg in Environment.GetCommandLineArgs())
            {
                string a = arg.ToLowerInvariant();
                if (a == "/ru" || a == "-ru" || a == "--ru") return true;
                if (a == "/en" || a == "-en" || a == "--en") return false;
            }

            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";
        }

        static string T(string ru, string en) { return RU ? ru : en; }

        static readonly string DateFmt = T("dd.MM.yyyy HH:mm:ss", "yyyy-MM-dd HH:mm:ss");
        static readonly string DatePickFmt = T("dd.MM.yyyy", "yyyy-MM-dd");

        ListView list;
        DateTimePicker datePick, timePick;
        CheckBox chkCreated, chkModified, chkAccessed, chkMeta, chkZone;
        HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool DeleteFile(string name);

        public MainForm()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = T("Смена даты файлов", "File Date Changer");
            Font = new Font("Segoe UI", 9.5f);
            ClientSize = new Size(820, 540);
            MinimumSize = new Size(760, 480);
            StartPosition = FormStartPosition.CenterScreen;
            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;

            Button btnAdd = MakeButton(T("Добавить файлы", "Add files"), 10, 10, 150);
            btnAdd.Click += delegate { AddFilesDialog(); };
            Button btnFolder = MakeButton(T("Добавить папку", "Add folder"), 165, 10, 150);
            btnFolder.Click += delegate { AddFolderDialog(); };
            Button btnRemove = MakeButton(T("Убрать выбранные", "Remove selected"), 320, 10, 160);
            btnRemove.Click += delegate { RemoveSelected(); };
            Button btnClear = MakeButton(T("Очистить список", "Clear list"), 485, 10, 150);
            btnClear.Click += delegate { list.Items.Clear(); paths.Clear(); };

            Label hint = new Label();
            hint.Text = T("Можно просто перетащить файлы и папки мышкой в список ниже.", "You can simply drag files and folders into the list below.");
            hint.ForeColor = SystemColors.GrayText;
            hint.AutoSize = true;
            hint.Location = new Point(10, 50);
            Controls.Add(hint);

            list = new ListView();
            list.View = View.Details;
            list.FullRowSelect = true;
            list.GridLines = true;
            list.HideSelection = false;
            list.AllowDrop = true;
            list.Location = new Point(10, 74);
            list.Size = new Size(800, 286);
            list.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            list.Columns.Add(T("Файл", "File"), 330);
            list.Columns.Add(T("Создан", "Created"), 140);
            list.Columns.Add(T("Изменён", "Modified"), 140);
            list.Columns.Add(T("Результат", "Result"), 170);
            list.DragEnter += OnDragEnter;
            list.DragDrop += OnDragDrop;
            list.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Delete) RemoveSelected(); };
            list.Resize += delegate { FitColumns(); };
            Controls.Add(list);

            // --- Новая дата ---
            GroupBox grpDate = MakeGroup(T("Новая дата и время", "New date and time"), 10, 368, 300, 108);
            grpDate.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            datePick = new DateTimePicker();
            datePick.Format = DateTimePickerFormat.Custom;
            datePick.CustomFormat = DatePickFmt;
            datePick.Location = new Point(15, 28);
            datePick.Width = 130;
            timePick = new DateTimePicker();
            timePick.Format = DateTimePickerFormat.Custom;
            timePick.CustomFormat = "HH:mm:ss";
            timePick.ShowUpDown = true;
            timePick.Location = new Point(155, 28);
            timePick.Width = 125;
            Button btnFromFile = new Button();
            btnFromFile.Text = T("Взять дату с выбранного файла", "Take the date from the selected file");
            btnFromFile.Location = new Point(15, 66);
            btnFromFile.Size = new Size(265, 30);
            btnFromFile.Click += delegate { TakeDateFromSelected(); };
            grpDate.Controls.Add(datePick);
            grpDate.Controls.Add(timePick);
            grpDate.Controls.Add(btnFromFile);

            // --- Что менять ---
            GroupBox grpWhat = MakeGroup(T("Какие даты менять", "Which dates to change"), 320, 368, 200, 108);
            grpWhat.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            chkCreated = MakeCheck(T("Дата создания", "Creation date"), 15, 25, true);
            chkModified = MakeCheck(T("Дата изменения", "Modification date"), 15, 50, true);
            chkAccessed = MakeCheck(T("Дата открытия", "Access date"), 15, 75, true);
            grpWhat.Controls.Add(chkCreated);
            grpWhat.Controls.Add(chkModified);
            grpWhat.Controls.Add(chkAccessed);

            // --- Дополнительно ---
            GroupBox grpExtra = MakeGroup(T("Дополнительно", "Extras"), 530, 368, 280, 108);
            grpExtra.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            chkMeta = MakeCheck(T("Очистить метаданные внутри файла", "Clean metadata inside the file"), 15, 25, false);
            Label metaNote = new Label();
            metaNote.Text = "Word, Excel, PowerPoint, JPG, PNG";
            metaNote.ForeColor = SystemColors.GrayText;
            metaNote.AutoSize = true;
            metaNote.Location = new Point(33, 47);
            chkZone = MakeCheck(T("Убрать пометку «скачано из интернета»", "Remove the \"from the internet\" mark"), 15, 75, false);
            grpExtra.Controls.Add(chkMeta);
            grpExtra.Controls.Add(metaNote);
            grpExtra.Controls.Add(chkZone);

            Button btnApply = new Button();
            btnApply.Text = T("Применить", "Apply");
            btnApply.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            btnApply.Location = new Point(10, 486);
            btnApply.Size = new Size(800, 44);
            btnApply.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            btnApply.Click += delegate { Apply(); };
            Controls.Add(btnApply);

            ResumeLayout(false);
            PerformLayout();
            FitColumns();
        }

        Button MakeButton(string text, int x, int y, int w)
        {
            Button b = new Button();
            b.Text = text;
            b.Location = new Point(x, y);
            b.Size = new Size(w, 32);
            Controls.Add(b);
            return b;
        }

        GroupBox MakeGroup(string text, int x, int y, int w, int h)
        {
            GroupBox g = new GroupBox();
            g.Text = text;
            g.Location = new Point(x, y);
            g.Size = new Size(w, h);
            Controls.Add(g);
            return g;
        }

        CheckBox MakeCheck(string text, int x, int y, bool on)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.Location = new Point(x, y);
            c.AutoSize = true;
            c.Checked = on;
            return c;
        }

        void FitColumns()
        {
            if (list == null || list.Columns.Count < 4) return;
            int rest = list.Columns[1].Width + list.Columns[2].Width + list.Columns[3].Width;
            int w = list.ClientSize.Width - rest - 4;
            if (w > 150) list.Columns[0].Width = w;
        }

        // ---------- Добавление ----------

        void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        }

        void OnDragDrop(object sender, DragEventArgs e)
        {
            string[] dropped = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (dropped == null) return;
            bool hasDir = dropped.Any(Directory.Exists);
            bool recurse = hasDir && AskRecurse();
            foreach (string p in dropped) AddPath(p, recurse);
        }

        bool AskRecurse()
        {
            return MessageBox.Show(this,
                T("Добавить также все файлы внутри папки (включая подпапки)?\n\n«Нет» - менять дату только у самой папки.", "Add every file inside the folder as well, including subfolders?\n\nChoosing No changes the date of the folder itself only."),
                T("Папка", "Folder"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        void AddFilesDialog()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Multiselect = true;
                dlg.Title = T("Выберите файлы", "Select files");
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    foreach (string f in dlg.FileNames) AddPath(f, false);
            }
        }

        void AddFolderDialog()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = T("Выберите папку", "Select a folder");
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    AddPath(dlg.SelectedPath, AskRecurse());
            }
        }

        void AddPath(string p, bool recurse)
        {
            list.BeginUpdate();
            try
            {
                AddOne(p);
                if (recurse && Directory.Exists(p))
                {
                    try
                    {
                        foreach (string d in Directory.GetDirectories(p, "*", SearchOption.AllDirectories)) AddOne(d);
                        foreach (string f in Directory.GetFiles(p, "*", SearchOption.AllDirectories)) AddOne(f);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, T("Не удалось прочитать часть папки:\n", "Part of the folder could not be read:\n") + ex.Message, T("Внимание", "Warning"),
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
            finally { list.EndUpdate(); }
        }

        void AddOne(string p)
        {
            if (!File.Exists(p) && !Directory.Exists(p)) return;
            if (!paths.Add(p)) return;
            ListViewItem it = new ListViewItem(p);
            it.SubItems.Add("");
            it.SubItems.Add("");
            it.SubItems.Add("");
            it.Tag = p;
            RefreshItem(it);
            list.Items.Add(it);
        }

        void RemoveSelected()
        {
            for (int i = list.SelectedItems.Count - 1; i >= 0; i--)
            {
                ListViewItem it = list.SelectedItems[i];
                paths.Remove((string)it.Tag);
                list.Items.Remove(it);
            }
        }

        void RefreshItem(ListViewItem it)
        {
            string p = (string)it.Tag;
            try
            {
                bool dir = Directory.Exists(p);
                DateTime c = dir ? Directory.GetCreationTime(p) : File.GetCreationTime(p);
                DateTime m = dir ? Directory.GetLastWriteTime(p) : File.GetLastWriteTime(p);
                it.SubItems[1].Text = c.ToString(DateFmt);
                it.SubItems[2].Text = m.ToString(DateFmt);
            }
            catch { }
        }

        void TakeDateFromSelected()
        {
            if (list.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, T("Сначала выделите файл в списке.", "Select a file in the list first."), T("Подсказка", "Hint"));
                return;
            }
            string p = (string)list.SelectedItems[0].Tag;
            DateTime d = Directory.Exists(p) ? Directory.GetCreationTime(p) : File.GetCreationTime(p);
            datePick.Value = d.Date;
            timePick.Value = d;
        }

        // ---------- Применение ----------

        void Apply()
        {
            if (list.Items.Count == 0)
            {
                MessageBox.Show(this, T("Список пуст. Добавьте файлы кнопкой или перетащите их.", "The list is empty. Add files with the button, or drag them in."), T("Нет файлов", "No files"));
                return;
            }
            if (!chkCreated.Checked && !chkModified.Checked && !chkAccessed.Checked && !chkMeta.Checked && !chkZone.Checked)
            {
                MessageBox.Show(this, T("Не выбрано ни одного действия.", "No action has been selected."), T("Нечего делать", "Nothing to do"));
                return;
            }

            DateTime t = timePick.Value;
            DateTime dt = datePick.Value.Date.Add(new TimeSpan(t.Hour, t.Minute, t.Second));

            string question = string.Format(T("Обработать {0} элемент(ов)?\nНовая дата: {1}", "Process {0} item(s)?\nNew date: {1}"), list.Items.Count, dt.ToString(DateFmt));
            if (chkMeta.Checked) question += T("\n\nМетаданные будут удалены внутри файлов. Это необратимо, лучше иметь копию.", "\n\nMetadata inside the files will be removed. That cannot be undone, so keep a copy.");
            if (MessageBox.Show(this, question, T("Подтверждение", "Confirmation"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            Cursor = Cursors.WaitCursor;
            int ok = 0, fail = 0;
            // Сначала файлы, потом папки (от самых глубоких к верхним)
            List<ListViewItem> ordered = list.Items.Cast<ListViewItem>()
                .OrderBy(i => Directory.Exists((string)i.Tag) ? 1 : 0)
                .ThenByDescending(i => ((string)i.Tag).Length)
                .ToList();

            foreach (ListViewItem it in ordered)
            {
                try
                {
                    string note = Process((string)it.Tag, dt);
                    it.SubItems[3].Text = T("Готово", "Done") + note;
                    it.ForeColor = SystemColors.WindowText;
                    ok++;
                }
                catch (Exception ex)
                {
                    it.SubItems[3].Text = T("Ошибка: ", "Error: ") + ex.Message;
                    it.ForeColor = Color.Firebrick;
                    fail++;
                }
                RefreshItem(it);
                Application.DoEvents();
            }
            Cursor = Cursors.Default;

            string res = T("Успешно: ", "Succeeded: ") + ok;
            if (fail > 0) res += T("\nС ошибками: ", "\nFailed: ") + fail + T(" (причина в колонке «Результат»; часто файл открыт в другой программе)", " (the reason is in the Result column; often the file is open in another program)");
            MessageBox.Show(this, res, T("Готово", "Done"), MessageBoxButtons.OK, fail > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        string Process(string path, DateTime dt)
        {
            if (Directory.Exists(path))
            {
                if (chkCreated.Checked) Directory.SetCreationTime(path, dt);
                if (chkModified.Checked) Directory.SetLastWriteTime(path, dt);
                if (chkAccessed.Checked) Directory.SetLastAccessTime(path, dt);
                return "";
            }

            string note = "";
            FileAttributes attr = File.GetAttributes(path);
            bool ro = (attr & FileAttributes.ReadOnly) != 0;
            DateTime origC = File.GetCreationTime(path);
            DateTime origM = File.GetLastWriteTime(path);
            DateTime origA = File.GetLastAccessTime(path);

            if (ro) File.SetAttributes(path, attr & ~FileAttributes.ReadOnly);
            try
            {
                if (chkMeta.Checked) note = Metadata.Clean(path, dt);
                if (chkZone.Checked)
                {
                    if (DeleteFile(path + ":Zone.Identifier")) note += T(", пометка снята", ", mark removed");
                }
                // Даты ставим последними: очистка метаданных меняет файл.
                // Не отмеченные даты возвращаем как были.
                File.SetCreationTime(path, chkCreated.Checked ? dt : origC);
                File.SetLastWriteTime(path, chkModified.Checked ? dt : origM);
                File.SetLastAccessTime(path, chkAccessed.Checked ? dt : origA);
            }
            finally
            {
                if (ro) File.SetAttributes(path, attr);
            }
            return note;
        }
    }

    static class Metadata
    {
        static readonly string[] OfficeExt = { ".docx", ".docm", ".dotx", ".dotm", ".xlsx", ".xlsm", ".xltx", ".xltm",
                                               ".pptx", ".pptm", ".potx", ".ppsx", ".ppsm" };
        static readonly string[] Unsupported = { ".pdf", ".doc", ".xls", ".ppt", ".rtf", ".odt", ".ods", ".tif", ".tiff", ".heic", ".mp4", ".mov" };

        public static string Clean(string path, DateTime dt)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            try
            {
                if (OfficeExt.Contains(ext)) { CleanOffice(path, dt); return T(", метаданные очищены", ", metadata cleaned"); }
                if (ext == ".jpg" || ext == ".jpeg" || ext == ".jfif")
                    return RewriteIfChanged(path, StripJpeg) ? T(", метаданные удалены", ", metadata removed") : T(", метаданных не было", ", there was no metadata");
                if (ext == ".png")
                    return RewriteIfChanged(path, StripPng) ? T(", метаданные удалены", ", metadata removed") : T(", метаданных не было", ", there was no metadata");
                if (Unsupported.Contains(ext)) return T(", метаданные: формат не поддерживается", ", metadata: format not supported");
                return "";
            }
            catch (Exception ex)
            {
                return T(", метаданные НЕ очищены (", ", metadata NOT cleaned (") + ex.Message + ")";
            }
        }

        // ---------- Word / Excel / PowerPoint ----------

        static void CleanOffice(string path, DateTime dt)
        {
            XNamespace cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
            XNamespace dc = "http://purl.org/dc/elements/1.1/";
            XNamespace dcterms = "http://purl.org/dc/terms/";
            XNamespace ep = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";
            string iso = dt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

            using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Update))
            {
                RewriteXml(zip, "docProps/core.xml", delegate(XDocument doc)
                {
                    XElement r = doc.Root;
                    foreach (string n in new[] { "creator", "title", "subject", "description" })
                        r.Elements(dc + n).Remove();
                    foreach (string n in new[] { "lastModifiedBy", "lastPrinted", "keywords", "category", "contentStatus" })
                        r.Elements(cp + n).Remove();
                    foreach (string n in new[] { "created", "modified" })
                    {
                        XElement el = r.Element(dcterms + n);
                        if (el != null) el.Value = iso;
                    }
                    XElement rev = r.Element(cp + "revision");
                    if (rev != null) rev.Value = "1";
                });

                RewriteXml(zip, "docProps/app.xml", delegate(XDocument doc)
                {
                    foreach (string n in new[] { "Company", "Manager", "HyperlinkBase", "TotalTime" })
                        doc.Root.Elements(ep + n).Remove();
                });

                RewriteXml(zip, "docProps/custom.xml", delegate(XDocument doc)
                {
                    doc.Root.RemoveNodes();
                });

                // Внутри архива у каждой части тоже есть время - выравниваем
                if (dt.Year >= 1980 && dt.Year <= 2107)
                {
                    DateTimeOffset zt = new DateTimeOffset(dt);
                    foreach (ZipArchiveEntry e in zip.Entries) e.LastWriteTime = zt;
                }
            }
        }

        static void RewriteXml(ZipArchive zip, string name, Action<XDocument> edit)
        {
            ZipArchiveEntry entry = zip.GetEntry(name);
            if (entry == null) return;
            XDocument doc;
            using (Stream s = entry.Open()) doc = XDocument.Load(s);
            if (doc.Root == null) return;
            edit(doc);
            using (Stream s = entry.Open())
            {
                s.SetLength(0);
                doc.Save(s, SaveOptions.DisableFormatting);
            }
        }

        // ---------- Картинки ----------

        static bool RewriteIfChanged(string path, Func<byte[], byte[]> strip)
        {
            byte[] src = File.ReadAllBytes(path);
            byte[] dst = strip(src);
            if (dst.Length == src.Length) return false;
            File.WriteAllBytes(path, dst);
            return true;
        }

        // Удаляет EXIF/XMP (APP1), Photoshop/IPTC (APP13), Ducky (APP12), комментарии. Без пересжатия.
        static byte[] StripJpeg(byte[] d)
        {
            if (d.Length < 4 || d[0] != 0xFF || d[1] != 0xD8) throw new InvalidDataException(T("файл не похож на JPEG", "the file does not look like a JPEG"));
            MemoryStream o = new MemoryStream(d.Length);
            o.WriteByte(0xFF);
            o.WriteByte(0xD8);
            int p = 2;
            while (p + 1 < d.Length)
            {
                if (d[p] != 0xFF) throw new InvalidDataException(T("повреждённый JPEG", "damaged JPEG"));
                byte m = d[p + 1];
                if (m == 0xFF) { p++; continue; }
                if (m == 0xDA || m == 0xD9)
                {
                    o.Write(d, p, d.Length - p);
                    return o.ToArray();
                }
                if (p + 3 >= d.Length) break;
                int len = (d[p + 2] << 8) | d[p + 3];
                int end = p + 2 + len;
                if (len < 2 || end > d.Length) throw new InvalidDataException(T("повреждённый JPEG", "damaged JPEG"));
                bool drop = m == 0xE1 || m == 0xED || m == 0xEC || m == 0xFE;
                if (!drop) o.Write(d, p, end - p);
                p = end;
            }
            throw new InvalidDataException(T("повреждённый JPEG", "damaged JPEG"));
        }

        // Удаляет текстовые блоки, EXIF и время из PNG. Картинка не меняется.
        static byte[] StripPng(byte[] d)
        {
            byte[] sig = { 137, 80, 78, 71, 13, 10, 26, 10 };
            if (d.Length < 8) throw new InvalidDataException(T("файл не похож на PNG", "the file does not look like a PNG"));
            for (int i = 0; i < 8; i++)
                if (d[i] != sig[i]) throw new InvalidDataException(T("файл не похож на PNG", "the file does not look like a PNG"));
            MemoryStream o = new MemoryStream(d.Length);
            o.Write(d, 0, 8);
            int p = 8;
            while (p + 12 <= d.Length)
            {
                long len = ((long)d[p] << 24) | ((long)d[p + 1] << 16) | ((long)d[p + 2] << 8) | d[p + 3];
                string type = Encoding.ASCII.GetString(d, p + 4, 4);
                long total = 12 + len;
                if (p + total > d.Length) throw new InvalidDataException(T("повреждённый PNG", "damaged PNG"));
                bool drop = type == "tEXt" || type == "zTXt" || type == "iTXt" || type == "eXIf" || type == "tIME";
                if (!drop) o.Write(d, p, (int)total);
                p += (int)total;
                if (type == "IEND") break;
            }
            return o.ToArray();
        }
    }
}
