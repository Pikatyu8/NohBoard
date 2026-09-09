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
    using System;
    using System.Drawing;
    using System.Linq;
    using System.Windows.Forms;
    using Extra;
    using Hooking;
    using ThoNohT.NohBoard.Hooking.Interop;

    /// <summary>
    /// The settings form.
    /// </summary>
    public partial class SettingsForm : Form
    {
        private bool capturingKey = false;
        private int trapToggleKey;

        // Dynamic controls for Overlay / Window settings
        private GroupBox grpWindowOverlay;
        private CheckBox chkAlwaysOnTop;
        private CheckBox chkBorderless;
        private CheckBox chkTransparentBackground;
        private CheckBox chkClickThrough;
        private Label lblOpacity;
        private NumericUpDown udOpacity;

        /// <summary>
        /// Initializes a new instance of the <see cref="SettingsForm" /> class.
        /// </summary>
        public SettingsForm()
        {
            this.InitializeComponent();
            this.InitializeWindowOverlayControls();
        }

        /// <summary>
        /// Dynamically creates the window overlay controls to avoid modifying designer files.
        /// </summary>
        private void InitializeWindowOverlayControls()
        {
            var bottomButtons = this.Controls.OfType<Button>().Where(b => b.Top > 200).ToList();
            int shiftY = 160;

            this.Height += shiftY;
            foreach (var btn in bottomButtons)
            {
                btn.Top += shiftY;
            }

            int groupTop = bottomButtons.Any() ? bottomButtons.Min(b => b.Top) - shiftY : this.ClientSize.Height - shiftY - 40;

            this.grpWindowOverlay = new GroupBox
            {
                Text = "Window & Overlay",
                Location = new Point(12, groupTop),
                Size = new Size(this.ClientSize.Width - 24, 150),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
            };

            this.chkAlwaysOnTop = new CheckBox
            {
                Text = "Always on top",
                Location = new Point(15, 20),
                AutoSize = true
            };

            this.chkBorderless = new CheckBox
            {
                Text = "Borderless (hide title bar)",
                Location = new Point(15, 44),
                AutoSize = true
            };

            this.chkTransparentBackground = new CheckBox
            {
                Text = "Transparent background",
                Location = new Point(15, 68),
                AutoSize = true
            };

            this.chkClickThrough = new CheckBox
            {
                Text = "Click-Through (clicks pass to game) [F8 to toggle]",
                Location = new Point(15, 92),
                AutoSize = true
            };

            this.lblOpacity = new Label
            {
                Text = "Opacity (%):",
                Location = new Point(15, 120),
                AutoSize = true
            };

            this.udOpacity = new NumericUpDown
            {
                Location = new Point(95, 118),
                Size = new Size(60, 20),
                Minimum = 10,
                Maximum = 100,
                Value = 100
            };

            this.grpWindowOverlay.Controls.Add(this.chkAlwaysOnTop);
            this.grpWindowOverlay.Controls.Add(this.chkBorderless);
            this.grpWindowOverlay.Controls.Add(this.chkTransparentBackground);
            this.grpWindowOverlay.Controls.Add(this.chkClickThrough);
            this.grpWindowOverlay.Controls.Add(this.lblOpacity);
            this.grpWindowOverlay.Controls.Add(this.udOpacity);

            this.Controls.Add(this.grpWindowOverlay);
        }

        /// <summary>
        /// Handles the loading of the settings form, all control values are set.
        /// </summary>
        private void SettingsForm_Load(object sender, System.EventArgs e)
        {
            this.udMouseSensitivity.Value = GlobalSettings.Settings.MouseSensitivity;
            this.udScrollHold.Value = GlobalSettings.Settings.ScrollHold;

            this.chkTrapKeyboard.Checked = GlobalSettings.Settings.TrapKeyboard;
            this.chkTrapMouse.Checked = GlobalSettings.Settings.TrapMouse;

            this.trapToggleKey = GlobalSettings.Settings.TrapToggleKeyCode;
            this.txtToggleKey.Text = ((Keys)this.trapToggleKey).ToString();

            switch (GlobalSettings.Settings.Capitalization)
            {
                case CapitalizationMethod.Capitalize:
                    this.rdbAlwaysCaps.Checked = true;
                    break;
                case CapitalizationMethod.Lowercase:
                    this.rdbAlwaysLower.Checked = true;
                    break;
                case CapitalizationMethod.FollowKeys:
                    this.rdbFollowKeystate.Checked = true;
                    break;
            }

            this.chkFollowShiftCapsInsensitive.Enabled = !this.rdbFollowKeystate.Checked;
            this.chkFollowShiftCapsSensitive.Enabled = !this.rdbFollowKeystate.Checked;
            this.chkFollowShiftCapsInsensitive.Checked = GlobalSettings.Settings.FollowShiftForCapsInsensitive;
            this.chkFollowShiftCapsSensitive.Checked = GlobalSettings.Settings.FollowShiftForCapsSensitive;

            this.chkMouseFromCenter.Checked = GlobalSettings.Settings.MouseFromCenter;

            this.txtTitle.Text = GlobalSettings.Settings.WindowTitle;

            this.udPressHold.Value = GlobalSettings.Settings.PressHold;

            // Load overlay settings
            this.chkAlwaysOnTop.Checked = GlobalSettings.Settings.AlwaysOnTop;
            this.chkBorderless.Checked = GlobalSettings.Settings.Borderless;
            this.chkTransparentBackground.Checked = GlobalSettings.Settings.TransparentBackground;
            this.chkClickThrough.Checked = GlobalSettings.Settings.ClickThrough;
            this.udOpacity.Value = Math.Max(10, Math.Min(100, GlobalSettings.Settings.Opacity));

            this.SetToolTips();
        }

        /// <summary>
        /// Sets the tooltips for the settings controls.
        /// </summary>
        private void SetToolTips()
        {
            var nl = Environment.NewLine;

            var tooltip = new ToolTip();
            tooltip.SetToolTip(this.txtToggleKey, "Double click to change the toggle key for the mouse/keyboard trap.");

            tooltip.SetToolTip(
                this.udMouseSensitivity,
                "The sensitivity with which the mouse speed indicator reacts to mouse movement.");

            tooltip.SetToolTip(this.udScrollHold, "The time (in milliseconds) before a mouse scroll is released.");

            tooltip.SetToolTip(
                this.chkTrapKeyboard,
                "Check to let the keyboard be trapped by pressing the trap toggle key.");
            tooltip.SetToolTip(this.chkTrapMouse, "Check to let the mouse be trapped by pressing the trap toggle key.");

            tooltip.SetToolTip(
                this.rdbFollowKeystate,
                "Show capitalized letter when shift or caps is pressed, " + nl
                + "but normal letters when both or neither are pressed.");
            tooltip.SetToolTip(this.rdbAlwaysCaps, "Always show capitalized letters.");
            tooltip.SetToolTip(this.rdbAlwaysLower, "Always show lower-case letters.");

            tooltip.SetToolTip(this.chkFollowShiftCapsInsensitive,
                "When Always show capitalized or lower-case letters is selected, checking this means the shift " + nl +
                "key is still used to shift to the alternate text on the element, for all elements that are not " + nl +
                "sensitive to caps lock.");
            tooltip.SetToolTip(this.chkFollowShiftCapsSensitive,
                "When Always show capitalized or lower-case letters is selected, checking this means the shift " + nl +
                "key is still used to shift to the alternate text on the element, for all elements that are " + nl +
                "sensitive to caps lock.");

            tooltip.SetToolTip(
                this.chkMouseFromCenter,
                "Some games keep resetting the cursor position to the center of the screen. This setting uses " + nl +
                "this fact and compares the current mouse position to the center of the screen, rather than its " + nl +
                "last position.");

            tooltip.SetToolTip(
                this.txtTitle,
                "Fill in if you want a custom window title." + nl
                + "If left empty, the default window title of \"NohBoard + version number\" will be shown.");

            tooltip.SetToolTip(this.udPressHold, "TODO: Tooltip about holding presses.");

            tooltip.SetToolTip(this.chkAlwaysOnTop, "Keep the NohBoard window on top of games and other windows.");
            tooltip.SetToolTip(this.chkBorderless, "Hide title bar and borders. Drag the window by holding Left Mouse Button (when Click-Through is disabled).");
            tooltip.SetToolTip(this.chkTransparentBackground, "Makes the background color completely see-through, showing only keys.");
            tooltip.SetToolTip(this.chkClickThrough, "Clicks will pass through the window directly into games/apps. Press F8 anywhere to toggle.");
            tooltip.SetToolTip(this.udOpacity, "Overall window transparency from 10% to 100%.");
        }

        /// <summary>
        /// Handles clicking of the OK button, the new settings are applied and saved and the form is closed.
        /// </summary>
        private void OkButton_Click(object sender, System.EventArgs e)
        {
            GlobalSettings.Settings.MouseSensitivity = (int)this.udMouseSensitivity.Value;
            GlobalSettings.Settings.ScrollHold = (int)this.udScrollHold.Value;

            GlobalSettings.Settings.TrapKeyboard = this.chkTrapKeyboard.Checked;
            GlobalSettings.Settings.TrapMouse = this.chkTrapMouse.Checked;
            GlobalSettings.Settings.TrapToggleKeyCode = this.trapToggleKey;

            GlobalSettings.Settings.Capitalization = this.rdbFollowKeystate.Checked
                ? CapitalizationMethod.FollowKeys
                : this.rdbAlwaysLower.Checked
                    ? CapitalizationMethod.Lowercase
                    : CapitalizationMethod.Capitalize;
            GlobalSettings.Settings.FollowShiftForCapsInsensitive = this.chkFollowShiftCapsInsensitive.Checked;
            GlobalSettings.Settings.FollowShiftForCapsSensitive = this.chkFollowShiftCapsSensitive.Checked;

            GlobalSettings.Settings.MouseFromCenter = this.chkMouseFromCenter.Checked;

            Func<Rectangle, Point> getCenter = r => r.Location + new Size(r.Width / 2, r.Height / 2);
            MouseState.SetMouseFromCenter(GlobalSettings.Settings.MouseFromCenter, Screen.AllScreens.Select(x => (x.Bounds, getCenter(x.Bounds))).ToList());

            GlobalSettings.Settings.WindowTitle = this.txtTitle.Text;
            GlobalSettings.Settings.PressHold = (int)this.udPressHold.Value;

            // Save overlay settings
            GlobalSettings.Settings.AlwaysOnTop = this.chkAlwaysOnTop.Checked;
            GlobalSettings.Settings.Borderless = this.chkBorderless.Checked;
            GlobalSettings.Settings.TransparentBackground = this.chkTransparentBackground.Checked;
            GlobalSettings.Settings.ClickThrough = this.chkClickThrough.Checked;
            GlobalSettings.Settings.Opacity = (int)this.udOpacity.Value;

            GlobalSettings.Save();

            this.DialogResult = DialogResult.OK;
        }

        private void txtToggleKey_KeyUp(object sender, KeyEventArgs e)
        {
            if (!this.capturingKey) return;

            this.txtToggleKey.Text = e.KeyCode.ToString();
            this.trapToggleKey = (int)e.KeyCode;

            e.SuppressKeyPress = true;
            this.capturingKey = false;
            HookManager.EnableMouseHook();
            HookManager.EnableKeyboardHook();
        }

        private void txtToggleKey_DoubleClick(object sender, System.EventArgs e)
        {
            HookManager.DisableKeyboardHook();
            HookManager.DisableMouseHook();
            this.capturingKey = true;
            this.txtToggleKey.Text = "Press a key...";
        }

        private void rdbFollowKeystate_CheckedChanged(object sender, System.EventArgs e)
        {
            this.chkFollowShiftCapsInsensitive.Enabled = !this.rdbFollowKeystate.Checked;
            this.chkFollowShiftCapsSensitive.Enabled = !this.rdbFollowKeystate.Checked;
        }
    }
}
