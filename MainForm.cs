using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ScreenOcr
{
    internal sealed class ImageItem
    {
        public string Key = Guid.NewGuid().ToString("N");
        public string Name;
        public Bitmap Image;
        public string Text = "";
        public string Error;
        public bool Done;
        public ListViewItem ListItem;
    }

    public sealed class MainForm : Form
    {
        private const int HotkeyId = 0x5C01;
        private static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly AppSettings _s = AppSettings.Load();
        private readonly OcrService _ocr = new OcrService();

        // UI
        private ToolStrip _tool;
        private ToolStripButton _btnCapture, _btnOpen, _btnPaste, _btnOcrSel, _btnOcrAll,
                                _btnCopy, _btnSave, _btnExport, _btnRemove, _btnClear, _btnTop;
        private SplitContainer _splitMain, _splitRight;
        private ListView _list;
        private ImageList _thumbs;
        private PictureBox _preview;
        private TextBox _text;
        private FlowLayoutPanel _options;
        private ComboBox _cboLang;
        private NumericUpDown _numScale;
        private CheckBox _chkStripNum, _chkIndent, _chkTabs, _chkNormalize, _chkInvert, _chkAutoOcr, _chkAutoCopy;
        private TextBox _txtHotkey;
        private StatusStrip _statusStrip;
        private ToolStripStatusLabel _status;
        private ToolStripProgressBar _progress;
        private NotifyIcon _tray;

        // state
        private bool _exiting, _busy, _capturing, _suppressTextEvent, _balloonShown, _loadingOptions;
        private int _captureNo;

        public MainForm()
        {
            Text = "ScreenOcr";
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1060, 700);
            MinimumSize = new Size(760, 480);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            AllowDrop = true;
            TopMost = _s.AlwaysOnTop;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildUi();
            BuildTray();
            LoadOptionsToUi();

            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            KeyDown += OnFormKeyDown;
        }

        // =====================================================================
        //  UI
        // =====================================================================
        private void BuildUi()
        {
            _tool = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(4, 2, 4, 2) };
            _btnCapture = Btn("📷 Chụp vùng", "Chụp một vùng màn hình", (s, e) => StartCapture());
            _btnOpen = Btn("📂 Mở ảnh…", "Chọn một hoặc nhiều file ảnh (Ctrl+O)", (s, e) => OpenImages());
            _btnPaste = Btn("📋 Dán ảnh", "Dán ảnh từ clipboard (Ctrl+V)", (s, e) => PasteFromClipboard());
            _btnOcrSel = Btn("▶ OCR ảnh chọn", "Nhận dạng chữ các ảnh đang chọn (F5)", async (s, e) => await OcrItemsAsync(SelectedItems()));
            _btnOcrAll = Btn("⏩ OCR tất cả", "Nhận dạng chữ tất cả ảnh (Ctrl+F5)", async (s, e) => await OcrItemsAsync(AllItems()));
            _btnCopy = Btn("⧉ Copy", "Copy text đang hiển thị vào clipboard (Ctrl+Shift+C)", (s, e) => CopyText(_text.Text, false));
            _btnSave = Btn("💾 Lưu file…", "Lưu text đang hiển thị ra 1 file (Ctrl+S)", (s, e) => SaveToFile());
            _btnExport = Btn("🗂 Xuất từng file…", "Mỗi ảnh xuất ra 1 file .txt vào thư mục chọn", (s, e) => ExportEach());
            _btnRemove = Btn("✕ Xoá", "Xoá ảnh đang chọn (Delete)", (s, e) => RemoveItems(SelectedItems()));
            _btnClear = Btn("🗑 Xoá hết", "Xoá toàn bộ danh sách", (s, e) => RemoveItems(AllItems()));
            _btnTop = new ToolStripButton("📌 Luôn trên cùng") { CheckOnClick = true, Checked = _s.AlwaysOnTop, ToolTipText = "Cửa sổ luôn nổi trên các ứng dụng khác" };
            _btnTop.CheckedChanged += (s, e) => { TopMost = _btnTop.Checked; _s.AlwaysOnTop = _btnTop.Checked; _s.Save(); };

            _tool.Items.AddRange(new ToolStripItem[]
            {
                _btnCapture, _btnOpen, _btnPaste, new ToolStripSeparator(),
                _btnOcrSel, _btnOcrAll, new ToolStripSeparator(),
                _btnCopy, _btnSave, _btnExport, new ToolStripSeparator(),
                _btnRemove, _btnClear, new ToolStripSeparator(), _btnTop
            });

            // --- danh sách ảnh ---
            _thumbs = new ImageList { ImageSize = new Size(128, 80), ColorDepth = ColorDepth.Depth32Bit };
            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.LargeIcon,
                LargeImageList = _thumbs,
                HideSelection = false,
                MultiSelect = true,
                AllowDrop = true,
            };
            _list.SelectedIndexChanged += (s, e) => RefreshDetail();
            _list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete) { RemoveItems(SelectedItems()); e.Handled = true; }
                else if (e.Control && e.KeyCode == Keys.A)
                {
                    foreach (ListViewItem it in _list.Items) it.Selected = true;
                    e.Handled = true;
                }
            };
            _list.DragEnter += OnDragEnter;
            _list.DragDrop += OnDragDrop;

            // --- preview + text ---
            _preview = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = SystemColors.ControlDark };
            _text = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                AcceptsTab = true,
                Font = new Font("Consolas", 10.5f),
                MaxLength = 0,
            };
            _text.TextChanged += (s, e) =>
            {
                if (_suppressTextEvent) return;
                var sel = SelectedItems();
                if (sel.Count == 1) sel[0].Text = _text.Text; // cho phép sửa tay kết quả
            };

            _splitRight = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            _splitRight.Panel1.Controls.Add(_preview);
            _splitRight.Panel2.Controls.Add(_text);

            _splitMain = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, FixedPanel = FixedPanel.Panel1 };
            _splitMain.Panel1.Controls.Add(_list);
            _splitMain.Panel2.Controls.Add(_splitRight);

            // --- tuỳ chọn ---
            _options = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Padding = new Padding(6, 4, 6, 4),
            };
            _cboLang = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
            _numScale = new NumericUpDown { Minimum = 1, Maximum = 4, Width = 45 };
            _chkStripNum = Chk("Bỏ số dòng", "Bỏ số dòng ở lề trái (Eclipse: Show Line Numbers)");
            _chkIndent = Chk("Giữ thụt lề", "Dựng lại thụt lề từ vị trí chữ trên ảnh");
            _chkTabs = Chk("Thụt lề bằng Tab", "Dùng ký tự Tab (4 cột) thay vì dấu cách");
            _chkNormalize = Chk("Chuẩn hoá ký tự", "Chuyển ký tự full-width (（），；) và nháy cong về ASCII");
            _chkInvert = Chk("Tự đảo màu nền tối", "Giúp OCR tốt hơn với Eclipse Dark theme");
            _chkAutoOcr = Chk("Tự OCR sau khi chụp", null);
            _chkAutoCopy = Chk("Tự copy sau OCR", null);
            _txtHotkey = new TextBox { Width = 120, ReadOnly = true, BackColor = SystemColors.Window, Cursor = Cursors.Hand };
            _txtHotkey.KeyDown += OnHotkeyBoxKeyDown;
            var tip = new ToolTip();
            tip.SetToolTip(_txtHotkey, "Bấm vào ô rồi nhấn tổ hợp phím mới, VD: Ctrl+Shift+Q");
            tip.SetToolTip(_cboLang, "en-US: code thuần tiếng Anh (ký hiệu chính xác hơn)\nja: code có comment tiếng Nhật");

            _options.Controls.AddRange(new Control[]
            {
                Lbl("Ngôn ngữ:"), _cboLang, Lbl("Phóng to ×"), _numScale,
                _chkStripNum, _chkIndent, _chkTabs, _chkNormalize, _chkInvert,
                _chkAutoOcr, _chkAutoCopy, Lbl("Hotkey:"), _txtHotkey,
            });

            _cboLang.SelectedIndexChanged += (s, e) => SaveOptionsFromUi();
            _numScale.ValueChanged += (s, e) => SaveOptionsFromUi();

            // --- status ---
            _statusStrip = new StatusStrip();
            _status = new ToolStripStatusLabel("Sẵn sàng") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            _progress = new ToolStripProgressBar { Visible = false, Width = 160 };
            _statusStrip.Items.AddRange(new ToolStripItem[] { _status, _progress });

            // Thứ tự Add quan trọng: control Fill phải thêm trước
            Controls.Add(_splitMain);
            Controls.Add(_options);
            Controls.Add(_statusStrip);
            Controls.Add(_tool);
        }

        private ToolStripButton Btn(string text, string tip, EventHandler click)
        {
            var b = new ToolStripButton(text) { ToolTipText = tip, DisplayStyle = ToolStripItemDisplayStyle.Text };
            b.Click += click;
            return b;
        }

        private CheckBox Chk(string text, string tip)
        {
            var c = new CheckBox { Text = text, AutoSize = true, Margin = new Padding(8, 6, 0, 0) };
            if (tip != null) new ToolTip().SetToolTip(c, tip);
            c.CheckedChanged += (s, e) => SaveOptionsFromUi();
            return c;
        }

        private static Label Lbl(string text) =>
            new Label { Text = text, AutoSize = true, Margin = new Padding(8, 7, 2, 0) };

        private void BuildTray()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Mở ScreenOcr", null, (s, e) => ShowMain());
            menu.Items.Add("Chụp vùng", null, (s, e) => StartCapture());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Thoát", null, (s, e) => ExitApp());

            _tray = new NotifyIcon
            {
                Icon = Icon ?? SystemIcons.Application,
                Text = "ScreenOcr",
                Visible = true,
                ContextMenuStrip = menu,
            };
            _tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowMain(); };
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _splitMain.Panel1MinSize = 150;
            float dpi;
            using (var g = CreateGraphics()) dpi = g.DpiX;
            _splitMain.SplitterDistance = (int)(300 * dpi / 96.0);
            _splitRight.SplitterDistance = (int)(_splitRight.Height * 0.42);

            if (_cboLang.Items.Count == 0)
                SetStatus("⚠ Không tìm thấy gói OCR nào. Hãy cài gói OCR tiếng Anh/Nhật trong Windows.", true);
            else
                SetStatus($"Sẵn sàng — nhấn {_s.Hotkey} ở bất kỳ đâu để chụp vùng màn hình.");
        }

        // =====================================================================
        //  Tuỳ chọn
        // =====================================================================
        private void LoadOptionsToUi()
        {
            _loadingOptions = true;
            try
            {
                try
                {
                    foreach (var kv in OcrService.GetLanguages())
                        _cboLang.Items.Add(new LangItem(kv.Key, kv.Value));
                }
                catch (Exception ex)
                {
                    SetStatus("Không khởi tạo được Windows OCR: " + ex.Message, true);
                }

                var idx = _cboLang.Items.Cast<LangItem>().ToList()
                    .FindIndex(l => l.Tag.Equals(_s.LanguageTag, StringComparison.OrdinalIgnoreCase));
                if (idx < 0) idx = _cboLang.Items.Cast<LangItem>().ToList()
                    .FindIndex(l => l.Tag.StartsWith(_s.LanguageTag.Split('-')[0], StringComparison.OrdinalIgnoreCase));
                if (idx < 0 && _cboLang.Items.Count > 0) idx = 0;
                if (idx >= 0) _cboLang.SelectedIndex = idx;

                _numScale.Value = Math.Max(1, Math.Min(4, _s.Scale));
                _chkStripNum.Checked = _s.StripLineNumbers;
                _chkIndent.Checked = _s.KeepIndent;
                _chkTabs.Checked = _s.UseTabs;
                _chkNormalize.Checked = _s.NormalizeWidth;
                _chkInvert.Checked = _s.AutoInvertDark;
                _chkAutoOcr.Checked = _s.AutoOcrAfterCapture;
                _chkAutoCopy.Checked = _s.AutoCopyAfterOcr;
                _txtHotkey.Text = _s.Hotkey;
            }
            finally { _loadingOptions = false; }
        }

        private void SaveOptionsFromUi()
        {
            if (_loadingOptions) return;
            if (_cboLang.SelectedItem is LangItem li) _s.LanguageTag = li.Tag;
            _s.Scale = (int)_numScale.Value;
            _s.StripLineNumbers = _chkStripNum.Checked;
            _s.KeepIndent = _chkIndent.Checked;
            _s.UseTabs = _chkTabs.Checked;
            _s.NormalizeWidth = _chkNormalize.Checked;
            _s.AutoInvertDark = _chkInvert.Checked;
            _s.AutoOcrAfterCapture = _chkAutoOcr.Checked;
            _s.AutoCopyAfterOcr = _chkAutoCopy.Checked;
            _s.Save();
        }

        private sealed class LangItem
        {
            public readonly string Tag, Name;
            public LangItem(string tag, string name) { Tag = tag; Name = name; }
            public override string ToString() => $"{Name} ({Tag})";
        }

        // =====================================================================
        //  Hotkey toàn cục
        // =====================================================================
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RegisterGlobalHotkey(_s.Hotkey, false);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            NativeMethods.UnregisterHotKey(Handle, HotkeyId);
            base.OnHandleDestroyed(e);
        }

        private bool RegisterGlobalHotkey(string text, bool notify)
        {
            NativeMethods.UnregisterHotKey(Handle, HotkeyId);
            if (!HotkeyParser.TryParse(text, out var mods, out var key))
            {
                SetStatus($"Hotkey không hợp lệ: {text}", true);
                return false;
            }
            if (!NativeMethods.RegisterHotKey(Handle, HotkeyId, mods | NativeMethods.MOD_NOREPEAT, (uint)key))
            {
                SetStatus($"⚠ Hotkey {text} đang bị ứng dụng khác sử dụng. Hãy chọn tổ hợp khác.", true);
                return false;
            }
            if (notify) SetStatus($"Đã đặt hotkey: {text}");
            return true;
        }

        private void OnHotkeyBoxKeyDown(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            if (HotkeyParser.IsModifierKey(e.KeyCode)) return;
            if (e.Modifiers == Keys.None && !(e.KeyCode >= Keys.F1 && e.KeyCode <= Keys.F24) && e.KeyCode != Keys.PrintScreen)
            {
                SetStatus("Hotkey nên có Ctrl / Alt / Shift (hoặc dùng phím F1–F24, PrintScreen).", true);
                return;
            }

            var text = HotkeyParser.Format(e.Modifiers, e.KeyCode);
            if (RegisterGlobalHotkey(text, true))
            {
                _txtHotkey.Text = text;
                _s.Hotkey = text;
                _s.Save();
            }
            else
            {
                RegisterGlobalHotkey(_s.Hotkey, false); // khôi phục hotkey cũ
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                StartCapture();
                return;
            }
            base.WndProc(ref m);
        }

        // =====================================================================
        //  Chụp màn hình
        // =====================================================================
        private async void StartCapture()
        {
            if (_capturing || _busy) return;
            _capturing = true;
            try
            {
                bool wasVisible = Visible && WindowState != FormWindowState.Minimized;
                if (wasVisible) Hide();
                await Task.Delay(wasVisible ? 220 : 60); // chờ cửa sổ biến mất hẳn

                Bitmap region = null;
                using (var full = RegionSelectForm.CaptureVirtualScreen())
                using (var sel = new RegionSelectForm(full))
                {
                    if (sel.ShowDialog() == DialogResult.OK) region = sel.Result;
                }

                ShowMain();
                if (region == null) return;

                var item = AddImage(region, $"capture_{DateTime.Now:yyyyMMdd_HHmmss}_{++_captureNo}");
                SelectOnly(item);

                if (_s.AutoOcrAfterCapture)
                    await OcrItemsAsync(new List<ImageItem> { item });
            }
            catch (Exception ex)
            {
                ShowMain();
                SetStatus("Lỗi khi chụp: " + ex.Message, true);
            }
            finally { _capturing = false; }
        }

        // =====================================================================
        //  Nhập ảnh
        // =====================================================================
        private void OpenImages()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Chọn ảnh",
                Filter = "Ảnh|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|Tất cả|*.*",
                Multiselect = true,
            })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK) AddFiles(dlg.FileNames);
            }
        }

        private void AddFiles(IEnumerable<string> paths)
        {
            ImageItem last = null;
            int n = 0;
            foreach (var p in paths.Where(p => ImageExts.Contains(Path.GetExtension(p).ToLowerInvariant())))
            {
                try
                {
                    Bitmap bmp;
                    using (var fs = File.OpenRead(p))
                    using (var tmp = Image.FromStream(fs))
                        bmp = new Bitmap(tmp); // copy để không khoá file
                    last = AddImage(bmp, Path.GetFileNameWithoutExtension(p));
                    n++;
                }
                catch (Exception ex)
                {
                    SetStatus($"Không mở được {Path.GetFileName(p)}: {ex.Message}", true);
                }
            }
            if (last != null)
            {
                SelectOnly(last);
                SetStatus($"Đã thêm {n} ảnh.");
            }
        }

        private void PasteFromClipboard()
        {
            try
            {
                if (Clipboard.ContainsImage())
                {
                    using (var img = Clipboard.GetImage())
                    {
                        var item = AddImage(new Bitmap(img), $"clipboard_{DateTime.Now:yyyyMMdd_HHmmss}");
                        SelectOnly(item);
                    }
                }
                else if (Clipboard.ContainsFileDropList())
                {
                    AddFiles(Clipboard.GetFileDropList().Cast<string>());
                }
                else SetStatus("Clipboard không có ảnh.", true);
            }
            catch (Exception ex) { SetStatus("Không đọc được clipboard: " + ex.Message, true); }
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Bitmap))
                e.Effect = DragDropEffects.Copy;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files) AddFiles(files);
            else if (e.Data.GetData(DataFormats.Bitmap) is Image img)
                SelectOnly(AddImage(new Bitmap(img), $"drop_{DateTime.Now:HHmmss}"));
        }

        private ImageItem AddImage(Bitmap bmp, string name)
        {
            var item = new ImageItem { Name = name, Image = bmp };
            _thumbs.Images.Add(item.Key, MakeThumb(bmp, _thumbs.ImageSize));
            item.ListItem = new ListViewItem(name, item.Key) { Tag = item };
            _list.Items.Add(item.ListItem);
            return item;
        }

        private static Bitmap MakeThumb(Image src, Size size)
        {
            var t = new Bitmap(size.Width, size.Height);
            using (var g = Graphics.FromImage(t))
            {
                g.Clear(Color.White);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                double r = Math.Min((double)size.Width / src.Width, (double)size.Height / src.Height);
                int w = Math.Max(1, (int)(src.Width * r)), h = Math.Max(1, (int)(src.Height * r));
                g.DrawImage(src, (size.Width - w) / 2, (size.Height - h) / 2, w, h);
                g.DrawRectangle(Pens.Silver, 0, 0, size.Width - 1, size.Height - 1);
            }
            return t;
        }

        private void RemoveItems(List<ImageItem> items)
        {
            if (items.Count == 0 || _busy) return;
            if (items.Count > 1 && MessageBox.Show(this, $"Xoá {items.Count} ảnh khỏi danh sách?", "ScreenOcr",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            _preview.Image = null;
            _list.BeginUpdate();
            foreach (var it in items)
            {
                _list.Items.Remove(it.ListItem);
                _thumbs.Images.RemoveByKey(it.Key);
                it.Image.Dispose();
            }
            _list.EndUpdate();
            RefreshDetail();
        }

        // =====================================================================
        //  OCR
        // =====================================================================
        private async Task OcrItemsAsync(List<ImageItem> items)
        {
            if (_busy) return;
            if (items.Count == 0) { SetStatus("Chưa có ảnh nào được chọn.", true); return; }
            if (!(_cboLang.SelectedItem is LangItem)) { SetStatus("Chưa có ngôn ngữ OCR.", true); return; }

            SetBusy(true, items.Count);
            int ok = 0;
            try
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var it = items[i];
                    SetStatus($"Đang OCR {i + 1}/{items.Count}: {it.Name}…");
                    try
                    {
                        it.Text = await _ocr.RecognizeAsync(it.Image, _s);
                        it.Error = null;
                        it.Done = true;
                        ok++;
                    }
                    catch (Exception ex)
                    {
                        it.Error = ex.Message;
                        it.Done = false;
                    }
                    it.ListItem.Text = (it.Done ? "✓ " : "✗ ") + it.Name;
                    _progress.Value = i + 1;
                }
            }
            finally { SetBusy(false, 0); }

            RefreshDetail();

            var failed = items.Where(x => x.Error != null).ToList();
            if (failed.Count > 0)
                SetStatus($"Xong {ok}/{items.Count} ảnh. Lỗi: {failed[0].Error}", true);
            else
                SetStatus($"Đã OCR xong {ok} ảnh.");

            if (_s.AutoCopyAfterOcr && ok > 0)
                CopyText(BuildText(items.Where(x => x.Done).ToList()), true);
        }

        private void SetBusy(bool busy, int total)
        {
            _busy = busy;
            foreach (ToolStripItem b in _tool.Items)
                if (b != _btnTop && b != _btnCopy) b.Enabled = !busy;
            _progress.Visible = busy;
            if (busy) { _progress.Maximum = Math.Max(1, total); _progress.Value = 0; }
            UseWaitCursor = busy;
        }

        // =====================================================================
        //  Hiển thị / xuất kết quả
        // =====================================================================
        private List<ImageItem> AllItems() =>
            _list.Items.Cast<ListViewItem>().Select(i => (ImageItem)i.Tag).ToList();

        private List<ImageItem> SelectedItems() =>
            _list.SelectedItems.Cast<ListViewItem>().OrderBy(i => i.Index).Select(i => (ImageItem)i.Tag).ToList();

        private void SelectOnly(ImageItem item)
        {
            _list.SelectedItems.Clear();
            item.ListItem.Selected = true;
            item.ListItem.Focused = true;
            item.ListItem.EnsureVisible();
        }

        private static string BuildText(List<ImageItem> items)
        {
            if (items.Count == 1) return items[0].Text ?? "";
            var sb = new StringBuilder();
            foreach (var it in items)
            {
                sb.Append("// ===== ").Append(it.Name).Append(" =====\r\n");
                sb.Append(it.Text ?? "").Append("\r\n\r\n");
            }
            return sb.ToString().TrimEnd() + "\r\n";
        }

        private void RefreshDetail()
        {
            var sel = SelectedItems();
            _preview.Image = sel.Count > 0 ? sel[0].Image : null;

            _suppressTextEvent = true;
            try
            {
                if (sel.Count == 0) { _text.Text = ""; _text.ReadOnly = false; }
                else if (sel.Count == 1)
                {
                    _text.ReadOnly = false;
                    _text.Text = sel[0].Error != null ? "" : sel[0].Text;
                }
                else
                {
                    _text.ReadOnly = true; // nhiều ảnh: chỉ xem, chọn 1 ảnh để sửa
                    _text.Text = BuildText(sel);
                }
            }
            finally { _suppressTextEvent = false; }
        }

        private void CopyText(string text, bool auto)
        {
            if (string.IsNullOrEmpty(text)) { if (!auto) SetStatus("Không có text để copy.", true); return; }
            try
            {
                Clipboard.SetDataObject(text, true, 10, 100);
                SetStatus(auto ? "Đã OCR và copy vào clipboard ✔" : "Đã copy vào clipboard ✔");
            }
            catch (Exception ex) { SetStatus("Không copy được: " + ex.Message, true); }
        }

        private void SaveToFile()
        {
            var text = _text.Text;
            if (string.IsNullOrEmpty(text)) { SetStatus("Không có text để lưu.", true); return; }

            var sel = SelectedItems();
            using (var dlg = new SaveFileDialog
            {
                Title = "Lưu text",
                Filter = "Text (*.txt)|*.txt|Java (*.java)|*.java|Tất cả (*.*)|*.*",
                FileName = sel.Count == 1 ? sel[0].Name + ".txt" : "ocr_result.txt",
                InitialDirectory = Directory.Exists(_s.LastSaveDir) ? _s.LastSaveDir : "",
                OverwritePrompt = true,
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(dlg.FileName, text, Utf8NoBom);
                    _s.LastSaveDir = Path.GetDirectoryName(dlg.FileName);
                    _s.Save();
                    SetStatus("Đã lưu: " + dlg.FileName);
                }
                catch (Exception ex) { SetStatus("Lỗi khi lưu: " + ex.Message, true); }
            }
        }

        private void ExportEach()
        {
            var items = SelectedItems();
            if (items.Count <= 1) items = AllItems(); // không chọn nhiều -> xuất tất cả
            items = items.Where(i => i.Done && !string.IsNullOrEmpty(i.Text)).ToList();
            if (items.Count == 0) { SetStatus("Chưa có ảnh nào được OCR.", true); return; }

            using (var dlg = new FolderBrowserDialog
            {
                Description = $"Chọn thư mục để xuất {items.Count} file .txt",
                SelectedPath = Directory.Exists(_s.LastSaveDir) ? _s.LastSaveDir : "",
                ShowNewFolderButton = true,
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                int n = 0;
                foreach (var it in items)
                {
                    try
                    {
                        var path = UniquePath(dlg.SelectedPath, SafeFileName(it.Name), ".txt");
                        File.WriteAllText(path, it.Text, Utf8NoBom);
                        n++;
                    }
                    catch (Exception ex) { SetStatus($"Lỗi khi xuất {it.Name}: {ex.Message}", true); }
                }
                _s.LastSaveDir = dlg.SelectedPath;
                _s.Save();
                SetStatus($"Đã xuất {n} file vào {dlg.SelectedPath}");
            }
        }

        private static string SafeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        private static string UniquePath(string dir, string baseName, string ext)
        {
            var path = Path.Combine(dir, baseName + ext);
            for (int i = 1; File.Exists(path); i++) path = Path.Combine(dir, $"{baseName}_{i}{ext}");
            return path;
        }

        // =====================================================================
        //  Phím tắt trong cửa sổ
        // =====================================================================
        private async void OnFormKeyDown(object sender, KeyEventArgs e)
        {
            bool inText = ActiveControl == _text || ActiveControl == _txtHotkey;

            if (e.Control && e.KeyCode == Keys.V && !inText) { PasteFromClipboard(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.O) { OpenImages(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.S) { SaveToFile(); e.Handled = true; }
            else if (e.Control && e.Shift && e.KeyCode == Keys.C) { CopyText(_text.Text, false); e.Handled = true; }
            else if (e.KeyCode == Keys.F5)
            {
                e.Handled = true;
                await OcrItemsAsync(e.Control ? AllItems() : SelectedItems());
            }
        }

        // =====================================================================
        //  Khay hệ thống / thoát
        // =====================================================================
        private void ShowMain()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            TopMost = _s.AlwaysOnTop;
            Activate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_exiting && e.CloseReason == CloseReason.UserClosing)
            {
                // Nút X -> thu nhỏ xuống khay, app vẫn chạy để dùng hotkey
                e.Cancel = true;
                Hide();
                if (!_balloonShown)
                {
                    _tray.ShowBalloonTip(3000, "ScreenOcr vẫn đang chạy",
                        $"Nhấn {_s.Hotkey} để chụp. Chuột phải icon ở khay → Thoát để tắt hẳn.", ToolTipIcon.Info);
                    _balloonShown = true;
                }
                return;
            }
            base.OnFormClosing(e);
        }

        private void ExitApp()
        {
            _exiting = true;
            _s.Save();
            _tray.Visible = false;
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tray?.Dispose();
                foreach (var it in AllItems()) it.Image.Dispose();
            }
            base.Dispose(disposing);
        }

        private void SetStatus(string text, bool warn = false)
        {
            _status.Text = text;
            _status.ForeColor = warn ? Color.Firebrick : SystemColors.ControlText;
        }
    }
}
