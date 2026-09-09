/*
Copyright (C) 2016 by Eric Bataille <e.c.p.bataille@gmail.com>

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 2 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

namespace ThoNohT.NohBoard.Forms
{
    using Extra;
    using Hooking;
    using Hooking.Interop;
    using Keyboard;
    using Keyboard.ElementDefinitions;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Drawing;
    using System.Drawing.Text;
    using System.Linq;
    using System.Net.Http;
    using System.Runtime.InteropServices;
    using System.Runtime.Serialization.Json;
    using System.Text;
    using System.Threading.Tasks;
    using System.Windows.Forms;
    using System.Xml;
    using ThoNohT.NohBoard.Keyboard.Styles;
    using Version = NohBoard.Version;

    /// <summary>
    /// The main form.
    /// </summary>
    public partial class MainForm : Form
    {
        #region Win32 & Shell Imports

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;

        private const int WS_SYSMENU = 0x00080000;
        private const int WS_MINIMIZEBOX = 0x00020000;

        private const int WM_SETICON = 0x0080;
        private const int ICON_SMALL = 0;
        private const int ICON_BIG = 1;

        private const uint LWA_COLORKEY = 0x00000001;
        private const uint LWA_ALPHA = 0x00000002;

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetCapture();

        [ComImport]
        [Guid("56FDF342-FD9D-11d0-7586-00A0C958A0C2")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITaskbarList
        {
            void HrInit();
            void AddTab(IntPtr hWnd);
            void DeleteTab(IntPtr hWnd);
            void ActivateTab(IntPtr hWnd);
            void SetActiveAlt(IntPtr hWnd);
        }

        #endregion Win32 & Shell Imports

        #region Fields

        private readonly Dictionary<bool, Dictionary<bool, Brush>> backBrushes =
            new Dictionary<bool, Dictionary<bool, Brush>>();

        private ElementDefinition elementUnderCursor = null;
        private VersionInfo latestVersion = null;

        // Context menu items
        private ToolStripMenuItem mnuWindowMenu;
        private ToolStripMenuItem mnuAlwaysOnTop;
        private ToolStripMenuItem mnuBorderless;
        private ToolStripMenuItem mnuTransparentBg;
        private ToolStripMenuItem mnuClickThrough;

        // Tray icon
        private NotifyIcon trayIcon;

        // Prevents forcing window on top when dialogs are shown
        private bool isDialogOpen = false;

        #endregion Fields

        #region Constructors

        public MainForm()
        {
            // Загружаем настройки до создания дескриптора окна, чтобы сразу применить нужный стиль рамки
            GlobalSettings.Load();

            this.InitializeComponent();
            this.SetStyle(ControlStyles.ResizeRedraw, true);

            this.ShowInTaskbar = true;
            this.ShowIcon = true;

            // Если в настройках включен Borderless, сразу стартуем без рамки
            bool isBorderless = GlobalSettings.Settings != null && GlobalSettings.Settings.Borderless;
            this.FormBorderStyle = isBorderless ? FormBorderStyle.None : FormBorderStyle.FixedSingle;

            try
            {
                if (System.IO.File.Exists("NohBoard2.ico"))
                    this.Icon = new Icon("NohBoard2.ico");
                else
                    this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { }

            this.InitializeWindowContextMenu();
            this.InitializeTrayIcon();

            this.mnuToggleEditMode.Click += (s, e) => this.BeginInvoke((Action)this.ApplyWindowStyles);
            this.mnuToggleEditMode.CheckedChanged += (s, e) => this.BeginInvoke((Action)this.ApplyWindowStyles);

            this.MainMenu.Closed += (s, e) =>
            {
                this.menuOpen = false;
                this.BeginInvoke((Action)this.ApplyWindowStyles);
            };
        }

        private void InitializeTrayIcon()
        {
            this.trayIcon = new NotifyIcon
            {
                Icon = this.Icon,
                Text = "NohBoard (Double click to toggle Click-Through)",
                Visible = true,
                ContextMenuStrip = this.MainMenu
            };

            this.trayIcon.DoubleClick += (s, e) =>
            {
                GlobalSettings.Settings.ClickThrough = !GlobalSettings.Settings.ClickThrough;
                GlobalSettings.Save();
                this.ApplyWindowStyles();
            };
        }

        private void InitializeWindowContextMenu()
        {
            this.mnuWindowMenu = new ToolStripMenuItem("&Window");

            this.mnuAlwaysOnTop = new ToolStripMenuItem("Always on &Top") { CheckOnClick = true };
            this.mnuAlwaysOnTop.Click += (s, e) =>
            {
                GlobalSettings.Settings.AlwaysOnTop = this.mnuAlwaysOnTop.Checked;
                GlobalSettings.Save();
                this.ApplyWindowStyles();
            };

            this.mnuBorderless = new ToolStripMenuItem("&Borderless (no title bar)") { CheckOnClick = true };
            this.mnuBorderless.Click += (s, e) =>
            {
                GlobalSettings.Settings.Borderless = this.mnuBorderless.Checked;
                GlobalSettings.Save();
                this.ApplyWindowStyles();
            };

            this.mnuTransparentBg = new ToolStripMenuItem("Transparent &Background") { CheckOnClick = true };
            this.mnuTransparentBg.Click += (s, e) =>
            {
                GlobalSettings.Settings.TransparentBackground = this.mnuTransparentBg.Checked;
                GlobalSettings.Save();
                this.ApplyWindowStyles();
            };

            this.mnuClickThrough = new ToolStripMenuItem("&Click-Through (Pass clicks)") { CheckOnClick = true };
            this.mnuClickThrough.Click += (s, e) =>
            {
                GlobalSettings.Settings.ClickThrough = this.mnuClickThrough.Checked;
                GlobalSettings.Save();
                this.ApplyWindowStyles();
            };

            this.mnuWindowMenu.DropDownItems.Add(this.mnuAlwaysOnTop);
            this.mnuWindowMenu.DropDownItems.Add(this.mnuBorderless);
            this.mnuWindowMenu.DropDownItems.Add(this.mnuTransparentBg);
            this.mnuWindowMenu.DropDownItems.Add(this.mnuClickThrough);

            int insertIndex = Math.Max(0, this.MainMenu.Items.Count - 2);
            this.MainMenu.Items.Insert(insertIndex, this.mnuWindowMenu);
        }

        #endregion Constructors

        #region Window Styles, Taskbar & Dragging

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;

                // WS_SYSMENU критически важен: без него Windows 10/11 не показывает иконку
                // в таскбаре у окон без заголовка (FormBorderStyle.None)
                cp.Style |= WS_SYSMENU;
                cp.Style |= WS_MINIMIZEBOX;

                // Удаляем WS_EX_TOOLWINDOW и принудительно задаем WS_EX_APPWINDOW
                cp.ExStyle &= ~0x00000080;
                cp.ExStyle |= 0x00040000;
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            this.RegisterTaskbarTab();
        }

        private void RegisterTaskbarTab()
        {
            try
            {
                var taskbarListType = Type.GetTypeFromCLSID(new Guid("56FDF344-FD9D-11d0-7586-00A0C958A0C2"));
                if (taskbarListType != null)
                {
                    var taskbarList = (ITaskbarList)Activator.CreateInstance(taskbarListType);
                    taskbarList.HrInit();
                    taskbarList.AddTab(this.Handle);
                }
            }
            catch { }

            if (this.Icon != null)
            {
                SendMessage(this.Handle, WM_SETICON, (IntPtr)ICON_SMALL, this.Icon.Handle);
                SendMessage(this.Handle, WM_SETICON, (IntPtr)ICON_BIG, this.Icon.Handle);
            }
        }

        private DialogResult ShowDialogOnTop(Form dialog)
        {
            this.isDialogOpen = true;
            bool wasTopMost = this.TopMost;
            try
            {
                this.TopMost = false;
                dialog.TopMost = true;
                dialog.StartPosition = FormStartPosition.CenterParent;
                return dialog.ShowDialog(this);
            }
            finally
            {
                this.isDialogOpen = false;
                if (wasTopMost)
                {
                    this.TopMost = true;
                    SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
            }
        }

        /// <summary>
        /// Устанавливает точный размер клиентской области формы под габариты клавиатуры.
        /// </summary>
        private void UpdateFormDimensions()
        {
            if (GlobalSettings.CurrentDefinition == null)
                return;

            this.ClientSize = new Size(
                GlobalSettings.CurrentDefinition.Width,
                GlobalSettings.CurrentDefinition.Height);
        }

        public void ApplyWindowStyles()
        {
            bool inEditMode = this.mnuToggleEditMode != null && this.mnuToggleEditMode.Checked;
            bool isBorderless = GlobalSettings.Settings != null && GlobalSettings.Settings.Borderless && !inEditMode;

            FormBorderStyle targetStyle = inEditMode
                ? FormBorderStyle.Sizable
                : (isBorderless ? FormBorderStyle.None : FormBorderStyle.FixedSingle);

            if (this.FormBorderStyle != targetStyle)
            {
                Point loc = this.Location;
                this.FormBorderStyle = targetStyle;
                this.Location = loc;
                this.RegisterTaskbarTab();
            }

            this.UpdateFormDimensions();

            // Always on top
            this.TopMost = GlobalSettings.Settings.AlwaysOnTop;
            if (GlobalSettings.Settings.AlwaysOnTop && !inEditMode && !this.isDialogOpen)
            {
                SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }

            // Корректная настройка прозрачности, фона и Click-Through
            this.ApplyLayeredStyles(inEditMode);

            this.Invalidate();
            this.Update();
        }

        private void ApplyLayeredStyles(bool inEditMode)
        {
            bool clickThrough = GlobalSettings.Settings.ClickThrough && !inEditMode;
            bool transparentBg = GlobalSettings.Settings.TransparentBackground && GlobalSettings.CurrentStyle != null && !inEditMode;
            int opacityPercent = Math.Max(10, Math.Min(100, GlobalSettings.Settings.Opacity));
            bool semiTransparent = opacityPercent < 100;

            bool needsLayered = clickThrough || transparentBg || semiTransparent;

            int exStyle = GetWindowLong(this.Handle, GWL_EXSTYLE);

            if (needsLayered)
            {
                exStyle |= WS_EX_LAYERED;
                if (clickThrough)
                {
                    exStyle |= WS_EX_TRANSPARENT;
                }
                else
                {
                    exStyle &= ~WS_EX_TRANSPARENT;
                }

                SetWindowLong(this.Handle, GWL_EXSTYLE, exStyle);

                byte alpha = (byte)(opacityPercent * 255 / 100);

                if (transparentBg)
                {
                    Color bg = (Color)GlobalSettings.CurrentStyle.BackgroundColor;
                    this.BackColor = Color.FromArgb(255, bg);
                    uint crKey = (uint)(bg.R | (bg.G << 8) | (bg.B << 16));
                    SetLayeredWindowAttributes(this.Handle, crKey, alpha, LWA_COLORKEY | LWA_ALPHA);
                }
                else
                {
                    // ВАЖНО: Вызов SetLayeredWindowAttributes инициализирует DWM буфер окна!
                    // Без этого вызова окно при наличии WS_EX_LAYERED остается черным/пустым!
                    SetLayeredWindowAttributes(this.Handle, 0, alpha, LWA_ALPHA);
                }
            }
            else
            {
                // Полностью снимаем слоистый режим, если прозрачность и Click-Through выключены
                exStyle &= ~WS_EX_TRANSPARENT;
                exStyle &= ~WS_EX_LAYERED;
                SetWindowLong(this.Handle, GWL_EXSTYLE, exStyle);
            }

            if (this.mnuClickThrough != null)
            {
                this.mnuClickThrough.Checked = GlobalSettings.Settings.ClickThrough;
                this.mnuClickThrough.Enabled = !inEditMode;
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button == MouseButtons.Left && GlobalSettings.Settings.Borderless && !this.mnuToggleEditMode.Checked)
            {
                ReleaseCapture();
                SendMessage(this.Handle, Defines.WM_NCLBUTTONDOWN, (IntPtr)Defines.HTCAPTION, IntPtr.Zero);
            }
        }

        #endregion Window Styles, Taskbar & Dragging

        #region Version check

        public Task GetLatestVersion()
        {
            return new Task(
                () =>
                {
                    var updateUrl =
                        "https://gist.githubusercontent.com/ThoNohT/3181561f8148fb6b865f88714e975154/raw/nohboard_version.json";

                    VersionInfo downloadVersionInfo(string url)
                    {
                        var serializer = new DataContractJsonSerializer(typeof(VersionInfo));
                        using (var client = new HttpClient())
                        using (var reader = JsonReaderWriterFactory.CreateJsonReader(
                            client.GetStreamAsync(updateUrl).Result,
                            Encoding.UTF8,
                            XmlDictionaryReaderQuotas.Max,
                            dictionaryReader => { }))
                        {
                            return (VersionInfo)serializer.ReadObject(reader);
                        }
                    }

                    var versionInfo = downloadVersionInfo(updateUrl);

                    if ((versionInfo.Major > Version.Major) ||
                        (versionInfo.Major == Version.Major && versionInfo.Minor > Version.Minor) ||
                        (versionInfo.Major == Version.Major && versionInfo.Minor == Version.Minor
                         && versionInfo.Patch > Version.Patch))
                    {
                        this.latestVersion = versionInfo;
                    }
                });
        }

        #endregion Version check

        #region Keyboard loading and saving

        private List<SerializableFont> LoadKeyboard()
        {
            if (GlobalSettings.CurrentDefinition == null)
            {
                HookManager.DisableKeyboardHook();
                HookManager.DisableMouseHook();
                return new List<SerializableFont>();
            }

            if (GlobalSettings.CurrentDefinition.Elements.Any(x => !(x is KeyboardKeyDefinition)))
                HookManager.EnableMouseHook();
            else
                HookManager.DisableMouseHook();

            if (GlobalSettings.CurrentDefinition.Elements.Any(x => x is KeyboardKeyDefinition))
                HookManager.EnableKeyboardHook();
            else
                HookManager.DisableKeyboardHook();

            var missingFonts = this.CheckMissingFonts();
            GlobalSettings.Settings.InitUndoHistory();

            if (this.mnuToggleEditMode.Checked)
            {
                this.mnuToggleEditMode.Checked = false;
                this.mnuToggleEditMode_Click(null, null);
                this.ApplyWindowStyles();
            }

            this.currentlyManipulating = null;
            this.highlightedDefinition = null;
            this.selectedDefinition = null;

            this.UpdateFormDimensions();
            this.ResetBackBrushes();

            return missingFonts;
        }

        private List<SerializableFont> CheckMissingFonts()
        {
            var style = GlobalSettings.CurrentStyle;
            var usedFonts = style.ElementStyles.Values.OfType<KeyStyle>()
                .SelectMany(s => new[] { s.Loose?.Font, s.Pressed?.Font })
                .Union(new[] { style.DefaultKeyStyle?.Loose?.Font, style.DefaultKeyStyle?.Pressed?.Font })
                .Where(f => f != null).ToList();

            var installedFonts = new InstalledFontCollection();
            var installedFontFamilyNames = new HashSet<string>(installedFonts.Families.Select(f => f.Name));
            var notInstalledUsedFonts = usedFonts.Where(f => !installedFontFamilyNames.Contains(f.FontFamily)).ToList();

            foreach (var font in notInstalledUsedFonts)
            {
                font.AlternateFontFamily = SystemFonts.DefaultFont.FontFamily.Name;
            }

            if (!notInstalledUsedFonts.Any()) return new List<SerializableFont>();

            return notInstalledUsedFonts.OrderBy(f => f.DownloadUrl == null).Distinct(new SerializableFont.FamilyComparer()).ToList();
        }

        private void ResetBackBrushes()
        {
            GlobalSettings.StyleDependencyCounter++;

            foreach (var brush in this.backBrushes)
            {
                foreach (var b in brush.Value)
                    b.Value.Dispose();

                brush.Value.Clear();
            }
            this.backBrushes.Clear();

            foreach (var shift in new[] { false, true })
            {
                this.backBrushes.Add(shift, new Dictionary<bool, Brush>());

                foreach (var caps in new[] { false, true })
                {
                    var bmp = new Bitmap(
                        GlobalSettings.CurrentDefinition.Width,
                        GlobalSettings.CurrentDefinition.Height);
                    var g = Graphics.FromImage(bmp);

                    var cs = GlobalSettings.CurrentStyle;
                    if (cs.BackgroundImageFileName != null && FileHelper.StyleImageExists(cs.BackgroundImageFileName))
                    {
                        g.DrawImage(ImageCache.Get(cs.BackgroundImageFileName), this.ClientRectangle);
                    }

                    foreach (var def in GlobalSettings.CurrentDefinition.Elements)
                    {
                        if (def is KeyboardKeyDefinition) ((KeyboardKeyDefinition)def).Render(g, false, shift, caps);
                        if (def is MouseKeyDefinition) ((MouseKeyDefinition)def).Render(g, false, shift, caps);
                        if (def is MouseScrollDefinition) ((MouseScrollDefinition)def).Render(g, 0);
                    }

                    this.backBrushes[shift].Add(caps, new TextureBrush(bmp));
                }
            }

            this.Refresh();
        }

        private void mnuLoadKeyboard_Click(object sender, EventArgs e)
        {
            if (GlobalSettings.UnsavedDefinitionChanges || GlobalSettings.UnsavedStyleChanges)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Loading a new keyboard will undo them. Are you sure you want to load a new keyboard?",
                    "Discard changes",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning);

                if (result != DialogResult.OK) return;
            }

            this.menuOpen = false;

            using (var manageForm = new LoadKeyboardForm())
            {
                manageForm.DefinitionChanged += (kbDef, kbStyle, globalStyle) =>
                {
                    var backupDef = GlobalSettings.CurrentDefinition;
                    var backupStyle = GlobalSettings.CurrentStyle;

                    var backupCat = GlobalSettings.Settings.LoadedCategory;
                    var backupKb = GlobalSettings.Settings.LoadedKeyboard;
                    var backupKbStyle = GlobalSettings.Settings.LoadedStyle;
                    var backupkbGlobalStyle = GlobalSettings.Settings.LoadedGlobalStyle;

                    GlobalSettings.Settings.UpdateDefinition(kbDef, false);
                    GlobalSettings.Settings.UpdateStyle(kbStyle ?? new KeyboardStyle(), false);

                    GlobalSettings.Settings.LoadedCategory = kbDef.Category;
                    GlobalSettings.Settings.LoadedKeyboard = kbDef.Name;
                    GlobalSettings.Settings.LoadedStyle = kbStyle?.Name;
                    GlobalSettings.Settings.LoadedGlobalStyle = globalStyle;

                    try
                    {
                        var missingFonts = this.LoadKeyboard();
                        manageForm.ToggleFontsPanel(missingFonts);
                    }
                    catch (Exception ex)
                    {
                        GlobalSettings.Settings.UpdateDefinition(backupDef, false);
                        GlobalSettings.Settings.UpdateStyle(backupStyle, false);

                        GlobalSettings.Settings.LoadedCategory = backupCat;
                        GlobalSettings.Settings.LoadedKeyboard = backupKb;
                        GlobalSettings.Settings.LoadedStyle = backupKbStyle;
                        GlobalSettings.Settings.LoadedGlobalStyle = backupkbGlobalStyle;

                        this.LoadKeyboard();

                        MessageBox.Show(ex.Message + Environment.NewLine + "Reverted keyboard change.");
                    }
                };

                this.ShowDialogOnTop(manageForm);
            }
        }

        private void mnuSaveDefinitionAsName_Click(object sender, EventArgs e)
        {
            this.menuOpen = false;
            GlobalSettings.CurrentDefinition.Save();
            GlobalSettings.Settings.LoadedCategory = GlobalSettings.CurrentDefinition.Category;
            GlobalSettings.Settings.LoadedKeyboard = GlobalSettings.CurrentDefinition.Name;
        }

        private void mnuSaveDefinitionAs_Click(object sender, EventArgs e)
        {
            this.menuOpen = false;
            using (var saveForm = new SaveKeyboardAsForm())
            {
                this.ShowDialogOnTop(saveForm);
            }
        }

        #endregion Keyboard loading and saving

        #region Settings

        private void MainForm_Load(object sender, EventArgs e)
        {
            if (GlobalSettings.Settings == null)
            {
                if (!GlobalSettings.Load())
                {
                    MessageBox.Show(
                        this,
                        $"Failed to load the settings: {GlobalSettings.Errors}",
                        "Failed to load settings");
                }
            }

            this.Location = new Point(GlobalSettings.Settings.X, GlobalSettings.Settings.Y);
            var title = GlobalSettings.Settings.WindowTitle;
            this.Text = string.IsNullOrWhiteSpace(title) ? $"NohBoard {Version.Get}" : title;

            this.GetLatestVersion().Start();

            if (GlobalSettings.Settings.LoadedKeyboard != null && GlobalSettings.Settings.LoadedCategory != null)
            {
                try
                {
                    GlobalSettings.Settings.UpdateDefinition(KeyboardDefinition
                        .Load(GlobalSettings.Settings.LoadedCategory, GlobalSettings.Settings.LoadedKeyboard), false);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "There was an error loading the saved keyboard definition file:" +
                        $"{Environment.NewLine}{ex.Message}");
                    GlobalSettings.Settings.LoadedCategory = null;
                    GlobalSettings.Settings.LoadedKeyboard = null;
                }
            }

            if (GlobalSettings.CurrentDefinition != null && GlobalSettings.Settings.LoadedStyle != null)
            {
                try
                {
                    GlobalSettings.Settings.UpdateStyle(KeyboardStyle.Load(
                        GlobalSettings.Settings.LoadedStyle,
                        GlobalSettings.Settings.LoadedGlobalStyle), false);
                    this.LoadKeyboard();
                    this.ResetBackBrushes();
                }
                catch
                {
                    GlobalSettings.Settings.LoadedStyle = null;
                    MessageBox.Show(
                        $"Failed to load style {GlobalSettings.Settings.LoadedStyle}, loading default style.",
                        "Error loading style.");
                }

                if (GlobalSettings.CurrentDefinition.Elements.Any(x => !(x is KeyboardKeyDefinition)))
                    HookManager.EnableMouseHook();

                if (GlobalSettings.CurrentDefinition.Elements.Any(x => x is KeyboardKeyDefinition))
                    HookManager.EnableKeyboardHook();
            }

            this.UpdateTimer.Interval = GlobalSettings.Settings.UpdateInterval;
            this.UpdateTimer.Enabled = true;
            this.KeyCheckTimer.Enabled = true;

            this.Activate();
            this.ApplySettings();
        }

        private void MainForm_Move(object sender, EventArgs e)
        {
            if (GlobalSettings.Settings != null && this.WindowState == FormWindowState.Normal)
            {
                GlobalSettings.Settings.X = this.Location.X;
                GlobalSettings.Settings.Y = this.Location.Y;

                if (GlobalSettings.Settings.AlwaysOnTop && !this.mnuToggleEditMode.Checked && !this.isDialogOpen)
                {
                    SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (GlobalSettings.UnsavedDefinitionChanges || GlobalSettings.UnsavedStyleChanges && !CrashHandler.Crashed)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. If you exit now you will lose them. Are you sure you want to exit?",
                    "Discard changes",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning);

                if (result != DialogResult.OK)
                {
                    e.Cancel = true;
                    return;
                }
            }

            if (this.trayIcon != null)
            {
                this.trayIcon.Visible = false;
                this.trayIcon.Dispose();
            }

            HookManager.DisableMouseHook();
            HookManager.DisableKeyboardHook();

            GlobalSettings.Save();
        }

        private void ApplySettings()
        {
            HookManager.TrapKeyboard = GlobalSettings.Settings.TrapKeyboard;
            HookManager.TrapMouse = GlobalSettings.Settings.TrapMouse;
            HookManager.TrapToggleKeyCode = GlobalSettings.Settings.TrapToggleKeyCode;
            HookManager.ScrollHold = GlobalSettings.Settings.ScrollHold;
            HookManager.PressHold = GlobalSettings.Settings.PressHold;

            var title = GlobalSettings.Settings.WindowTitle;
            this.Text = string.IsNullOrWhiteSpace(title) ? $"NohBoard {Version.Get}" : title;

            this.ApplyWindowStyles();
            this.LoadKeyboard();
        }

        private void mnuSettings_Click(object sender, EventArgs e)
        {
            this.menuOpen = false;

            using (var settingsForm = new SettingsForm())
            {
                var result = this.ShowDialogOnTop(settingsForm);
                if (result == DialogResult.Cancel)
                    return;

                this.ApplySettings();
            }
        }

        private void MainMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            this.menuOpen = true;

            bool inEditMode = this.mnuToggleEditMode.Checked;

            if (this.mnuAlwaysOnTop != null)
                this.mnuAlwaysOnTop.Checked = GlobalSettings.Settings.AlwaysOnTop;

            if (this.mnuBorderless != null)
            {
                this.mnuBorderless.Checked = GlobalSettings.Settings.Borderless;
                this.mnuBorderless.Enabled = !inEditMode;
            }

            if (this.mnuTransparentBg != null)
            {
                this.mnuTransparentBg.Checked = GlobalSettings.Settings.TransparentBackground;
                this.mnuTransparentBg.Enabled = !inEditMode;
            }

            if (this.mnuClickThrough != null)
            {
                this.mnuClickThrough.Checked = GlobalSettings.Settings.ClickThrough;
                this.mnuClickThrough.Enabled = !inEditMode;
            }

            this.mnuSaveDefinition.Enabled = GlobalSettings.CurrentDefinition != null;
            if (GlobalSettings.CurrentDefinition != null)
            {
                this.mnuSaveDefinitionAsName.Text =
                    $"Save &To '{GlobalSettings.CurrentDefinition.Category}/{GlobalSettings.CurrentDefinition.Name}'";

                var mousePos = this.PointToClient(Cursor.Position);
                this.elementUnderCursor =
                    GlobalSettings.CurrentDefinition.Elements.FirstOrDefault(x => x.Inside(mousePos));

                if (inEditMode && this.selectedDefinition == null)
                {
                    this.highlightedDefinition = this.elementUnderCursor;
                    this.highlightedDefinition?.StartManipulating(mousePos, false);
                }

                var relevantElement = this.selectedDefinition ?? this.elementUnderCursor;
                this.mnuEditElementStyle.Enabled = relevantElement != null;
                this.mnuElementProperties.Enabled = relevantElement != null;
            }

            this.mnuKeyboardProperties.Visible = inEditMode;
            this.mnuUpdateTextPosition.Visible = inEditMode;
            this.mnuElementProperties.Visible = inEditMode;
            this.mnuEditKeyboardStyle.Visible = inEditMode;
            this.mnuEditElementStyle.Visible = inEditMode;
            this.MainMenuSep1.Visible = inEditMode;

            this.mnuSaveStyleToName.Text = $"Save &To '{GlobalSettings.CurrentStyle.Name}'";
            this.mnuSaveStyleToName.Visible = !GlobalSettings.Settings.LoadedGlobalStyle;
            this.mnuSaveToGlobalStyleName.Text = $"Save To &Global '{GlobalSettings.CurrentStyle.Name}'";
            this.mnuSaveToGlobalStyleName.Enabled = GlobalSettings.CurrentStyle.IsGlobal;
            this.mnuSaveToGlobalStyleName.Visible = GlobalSettings.Settings.LoadedGlobalStyle;

            this.mnuToggleEditMode.Enabled = GlobalSettings.CurrentDefinition != null;

            if (this.latestVersion != null && !this.mnuUpdate.Visible)
            {
                this.mnuUpdate.Text = $"New version available: {this.latestVersion.Format()}.";
                this.mnuUpdate.Visible = true;
                this.mnuUpdate.Click += (s, ea) => { Process.Start(new ProcessStartInfo { FileName = "https://github.com/ThoNohT/NohBoard/releases", UseShellExecute = true }); };
            }

            this.mnuMoveElement.Visible = this.relevantDefinition != null;
            var highlightedSomething = inEditMode && this.relevantDefinition != null;

            this.mnuAddBoundaryPoint.Visible = highlightedSomething &&
                this.relevantDefinition.RelevantManipulation.Type == ElementManipulationType.MoveEdge;

            this.mnuRemoveBoundaryPoint.Visible = highlightedSomething &&
                this.relevantDefinition.RelevantManipulation.Type == ElementManipulationType.MoveBoundary;

            this.mnuRemoveElement.Visible = highlightedSomething;
            this.mnuAddElement.Visible = inEditMode && this.relevantDefinition == null;
        }

        private void MainForm_Deactivate(object sender, EventArgs e)
        {
            this.menuOpen = false;
        }

        private void mnuExit_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        #endregion Settings

        #region Rendering

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(GlobalSettings.CurrentStyle.BackgroundColor);

            if (GlobalSettings.CurrentDefinition == null || !this.backBrushes.Any())
                return;

            e.Graphics.FillRectangle(
                this.backBrushes[KeyboardState.ShiftDown][KeyboardState.CapsActive],
                new Rectangle(0, 0, GlobalSettings.CurrentDefinition.Width, GlobalSettings.CurrentDefinition.Height));

            KeyboardState.CheckKeyHolds(GlobalSettings.Settings.PressHold);
            var kbKeys = KeyboardState.PressedKeys;
            var mouseKeys = MouseState.PressedKeys.Select(k => (int)k).ToList();
            MouseState.CheckKeyHolds(GlobalSettings.Settings.PressHold);
            MouseState.CheckScrollAndMovement();
            var scrollCounts = MouseState.ScrollCounts;
            var allDefs = GlobalSettings.CurrentDefinition.Elements;
            foreach (var def in allDefs)
            {
                this.Render(e.Graphics, def, allDefs, kbKeys, mouseKeys, scrollCounts, false);
            }

            if (this.currentlyManipulating == null)
            {
                if (this.highlightedDefinition != this.selectedDefinition)
                    this.highlightedDefinition?.RenderHighlight(e.Graphics);

                if (this.selectedDefinition != null)
                {
                    this.Render(e.Graphics, this.selectedDefinition, allDefs, kbKeys, mouseKeys, scrollCounts, true);
                    this.selectedDefinition.RenderSelected(e.Graphics);
                }
            }
            else
            {
                this.currentlyManipulating.Value.definition.RenderEditing(e.Graphics);
            }

            base.OnPaint(e);
        }

        private void Render(
            Graphics g,
            ElementDefinition def,
            List<ElementDefinition> allDefs,
            IReadOnlyList<int> kbKeys,
            List<int> mouseKeys,
            IReadOnlyList<int> scrollCounts,
            bool alwaysRender)
        {
            if (def is KeyboardKeyDefinition kkDef)
            {
                var pressed = true;
                if (!kkDef.KeyCodes.Any() || !kkDef.KeyCodes.All(kbKeys.Contains)) pressed = false;

                if (kkDef.KeyCodes.Count == 1
                    && allDefs.OfType<KeyboardKeyDefinition>()
                        .Any(d => d.KeyCodes.Count > 1
                        && d.KeyCodes.All(kbKeys.Contains)
                        && d.KeyCodes.ContainsAll(kkDef.KeyCodes))) pressed = false;

                if (!pressed && !alwaysRender) return;

                kkDef.Render(g, pressed, KeyboardState.ShiftDown, KeyboardState.CapsActive);
            }
            if (def is MouseKeyDefinition mkDef)
            {
                var pressed = mouseKeys.Contains(mkDef.KeyCodes.Single());
                if (pressed || alwaysRender)
                    mkDef.Render(g, pressed, KeyboardState.ShiftDown, KeyboardState.CapsActive);
            }
            if (def is MouseScrollDefinition msDef)
            {
                var scrollCount = scrollCounts[msDef.KeyCodes.Single()];
                if (scrollCount > 0 || alwaysRender) msDef.Render(g, scrollCount);
            }
            if (def is MouseSpeedIndicatorDefinition)
            {
                ((MouseSpeedIndicatorDefinition)def).Render(g, MouseState.AverageSpeed);
            }
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            if (GlobalSettings.Settings != null && GlobalSettings.Settings.AlwaysOnTop &&
                !this.mnuToggleEditMode.Checked && !this.menuOpen && !this.isDialogOpen && GetCapture() == IntPtr.Zero)
            {
                SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }

            if (KeyboardState.Updated || MouseState.Updated)
                this.Refresh();
        }

        private void KeyCheckTimer_Tick(object sender, EventArgs e)
        {
            MouseState.CheckKeys(GlobalSettings.Settings.PressHold);
            KeyboardState.CheckKeys(GlobalSettings.Settings.PressHold);
        }

        #endregion Rendering

        private void mnuGenerateLog_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("This will crash NohBoard in order to generate a log, are you sure you want to do this?", "Generate crash log", MessageBoxButtons.OKCancel) == DialogResult.OK)
            {
                throw new Exception("A crash log was requested.");
            }
        }
    }
}
