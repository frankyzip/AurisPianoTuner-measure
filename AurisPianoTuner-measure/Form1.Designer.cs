namespace AurisPianoTuner_measure
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.SuspendLayout();
            
            // ===== FORM PROPERTIES =====
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 20F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1400, 900);
            this.Text = "Auris Piano Tuner - Measurement Tool";
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.MinimizeBox = true;
            this.ControlBox = true;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.BackColor = System.Drawing.Color.FromArgb(240, 240, 240);
            this.WindowState = System.Windows.Forms.FormWindowState.Maximized;
            this.MinimumSize = new System.Drawing.Size(800, 600);
            
            // ===== LEFT PANEL (CONTROLS) =====
            this.pnlControls = new System.Windows.Forms.Panel();
            this.pnlControls.Location = new System.Drawing.Point(10, 10);
            this.pnlControls.Size = new System.Drawing.Size(300, 850);
            this.pnlControls.BackColor = System.Drawing.Color.White;
            this.pnlControls.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pnlControls.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
            
            // --- ASIO Driver Section ---
            this.lblAsioDriver = new System.Windows.Forms.Label();
            this.lblAsioDriver.Text = "ASIO Driver:";
            this.lblAsioDriver.Location = new System.Drawing.Point(15, 20);
            this.lblAsioDriver.Size = new System.Drawing.Size(270, 25);
            this.lblAsioDriver.Font = new System.Drawing.Font("Arial", 10F, System.Drawing.FontStyle.Bold);
            
            this.cmbAsioDriver = new System.Windows.Forms.ComboBox();
            this.cmbAsioDriver.Location = new System.Drawing.Point(15, 50);
            this.cmbAsioDriver.Size = new System.Drawing.Size(270, 28);
            this.cmbAsioDriver.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbAsioDriver.Items.AddRange(new object[] { "ASIO4ALL", "Focusrite USB ASIO", "RME ASIO" });
            this.cmbAsioDriver.SelectedIndex = 0;
            this.cmbAsioDriver.Font = new System.Drawing.Font("Arial", 9F);
            
            // --- Piano Metadata GroupBox ---
            this.grpMetadata = new System.Windows.Forms.GroupBox();
            this.grpMetadata.Text = "Piano Metadata";
            this.grpMetadata.Location = new System.Drawing.Point(15, 100);
            this.grpMetadata.Size = new System.Drawing.Size(270, 220);
            this.grpMetadata.Font = new System.Drawing.Font("Arial", 10F, System.Drawing.FontStyle.Bold);
            
            // Piano Type Dropdown
            this.lblPianoType = new System.Windows.Forms.Label();
            this.lblPianoType.Text = "Piano Type:";
            this.lblPianoType.Location = new System.Drawing.Point(15, 30);
            this.lblPianoType.Size = new System.Drawing.Size(240, 25);
            this.lblPianoType.Font = new System.Drawing.Font("Arial", 9F, System.Drawing.FontStyle.Regular);
            
            this.cmbPianoType = new System.Windows.Forms.ComboBox();
            this.cmbPianoType.Location = new System.Drawing.Point(15, 55);
            this.cmbPianoType.Size = new System.Drawing.Size(240, 28);
            this.cmbPianoType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbPianoType.Font = new System.Drawing.Font("Arial", 9F);
            this.cmbPianoType.Items.AddRange(new object[] {
                "Spinet (95 cm)",
                "Console (107 cm)",
                "Studio (115 cm)",
                "Professional Upright (131 cm)",
                "Baby Grand (149 cm)",
                "Parlor Grand (180 cm)",
                "Semi-Concert Grand (215 cm)",
                "Concert Grand (274 cm)"
            });
            this.cmbPianoType.SelectedIndex = 5; // Parlor Grand default
            this.cmbPianoType.SelectedIndexChanged += new System.EventHandler(this.cmbPianoType_SelectedIndexChanged);
            
            // Custom Piano Type Input
            this.lblCustomType = new System.Windows.Forms.Label();
            this.lblCustomType.Text = "Custom Type (optional):";
            this.lblCustomType.Location = new System.Drawing.Point(15, 95);
            this.lblCustomType.Size = new System.Drawing.Size(240, 25);
            this.lblCustomType.Font = new System.Drawing.Font("Arial", 9F, System.Drawing.FontStyle.Regular);
            
            this.txtPianoType = new System.Windows.Forms.TextBox();
            this.txtPianoType.Location = new System.Drawing.Point(15, 120);
            this.txtPianoType.Size = new System.Drawing.Size(240, 25);
            this.txtPianoType.Text = "Yamaha C3";
            this.txtPianoType.Font = new System.Drawing.Font("Arial", 9F);
            
            // Piano Length
            this.lblPianoLength = new System.Windows.Forms.Label();
            this.lblPianoLength.Text = "Length (cm):";
            this.lblPianoLength.Location = new System.Drawing.Point(15, 155);
            this.lblPianoLength.Size = new System.Drawing.Size(110, 25);
            this.lblPianoLength.Font = new System.Drawing.Font("Arial", 9F, System.Drawing.FontStyle.Regular);
            
            this.numPianoLength = new System.Windows.Forms.NumericUpDown();
            this.numPianoLength.Location = new System.Drawing.Point(15, 180);
            this.numPianoLength.Size = new System.Drawing.Size(110, 25);
            this.numPianoLength.Minimum = 80;
            this.numPianoLength.Maximum = 300;
            this.numPianoLength.Value = 180;
            this.numPianoLength.Font = new System.Drawing.Font("Arial", 9F);
            
            // Scale Break
            this.lblScaleBreak = new System.Windows.Forms.Label();
            this.lblScaleBreak.Text = "Scale Break (MIDI):";
            this.lblScaleBreak.Location = new System.Drawing.Point(145, 155);
            this.lblScaleBreak.Size = new System.Drawing.Size(110, 25);
            this.lblScaleBreak.Font = new System.Drawing.Font("Arial", 9F, System.Drawing.FontStyle.Regular);
            
            this.numScaleBreak = new System.Windows.Forms.NumericUpDown();
            this.numScaleBreak.Location = new System.Drawing.Point(145, 180);
            this.numScaleBreak.Size = new System.Drawing.Size(110, 25);
            this.numScaleBreak.Minimum = 36;
            this.numScaleBreak.Maximum = 54;
            this.numScaleBreak.Value = 41;
            this.numScaleBreak.Font = new System.Drawing.Font("Arial", 9F);
            
            this.grpMetadata.Controls.Add(this.lblPianoType);
            this.grpMetadata.Controls.Add(this.cmbPianoType);
            this.grpMetadata.Controls.Add(this.lblCustomType);
            this.grpMetadata.Controls.Add(this.txtPianoType);
            this.grpMetadata.Controls.Add(this.lblPianoLength);
            this.grpMetadata.Controls.Add(this.numPianoLength);
            this.grpMetadata.Controls.Add(this.lblScaleBreak);
            this.grpMetadata.Controls.Add(this.numScaleBreak);
            
            // --- Control Buttons Section ---
            this.btnStart = new System.Windows.Forms.Button();
            this.btnStart.Text = "START MEASUREMENT";
            this.btnStart.Location = new System.Drawing.Point(15, 345);
            this.btnStart.Size = new System.Drawing.Size(270, 55);
            this.btnStart.BackColor = System.Drawing.Color.Green;
            this.btnStart.ForeColor = System.Drawing.Color.White;
            this.btnStart.Font = new System.Drawing.Font("Arial", 12F, System.Drawing.FontStyle.Bold);
            this.btnStart.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnStart.Click += new System.EventHandler(this.btnStart_Click);
            
            this.btnStop = new System.Windows.Forms.Button();
            this.btnStop.Text = "STOP MEASUREMENT";
            this.btnStop.Location = new System.Drawing.Point(15, 415);
            this.btnStop.Size = new System.Drawing.Size(270, 55);
            this.btnStop.BackColor = System.Drawing.Color.Red;
            this.btnStop.ForeColor = System.Drawing.Color.White;
            this.btnStop.Font = new System.Drawing.Font("Arial", 12F, System.Drawing.FontStyle.Bold);
            this.btnStop.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnStop.Enabled = false;
            this.btnStop.Click += new System.EventHandler(this.btnStop_Click);
            
            // --- File Operations Section ---
            this.btnSave = new System.Windows.Forms.Button();
            this.btnSave.Text = "SAVE MEASUREMENTS";
            this.btnSave.Location = new System.Drawing.Point(15, 495);
            this.btnSave.Size = new System.Drawing.Size(270, 45);
            this.btnSave.BackColor = System.Drawing.Color.FromArgb(70, 130, 180);
            this.btnSave.ForeColor = System.Drawing.Color.White;
            this.btnSave.Font = new System.Drawing.Font("Arial", 10F, System.Drawing.FontStyle.Bold);
            this.btnSave.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnSave.Click += new System.EventHandler(this.btnSave_Click);
            
            this.btnLoad = new System.Windows.Forms.Button();
            this.btnLoad.Text = "LOAD MEASUREMENTS";
            this.btnLoad.Location = new System.Drawing.Point(15, 555);
            this.btnLoad.Size = new System.Drawing.Size(270, 45);
            this.btnLoad.BackColor = System.Drawing.Color.FromArgb(70, 130, 180);
            this.btnLoad.ForeColor = System.Drawing.Color.White;
            this.btnLoad.Font = new System.Drawing.Font("Arial", 10F, System.Drawing.FontStyle.Bold);
            this.btnLoad.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnLoad.Click += new System.EventHandler(this.btnLoad_Click);

            // --- Version Label ---
            this.lblVersion = new System.Windows.Forms.Label();
            this.lblVersion.Text = "v1.0.1";
            this.lblVersion.Location = new System.Drawing.Point(15, 815);
            this.lblVersion.Size = new System.Drawing.Size(270, 25);
            this.lblVersion.ForeColor = System.Drawing.Color.Gray;
            this.lblVersion.Font = new System.Drawing.Font("Arial", 9F, System.Drawing.FontStyle.Regular);
            this.lblVersion.TextAlign = System.Drawing.ContentAlignment.BottomLeft;
            this.lblVersion.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;

            this.pnlControls.Controls.Add(this.lblAsioDriver);
            this.pnlControls.Controls.Add(this.cmbAsioDriver);
            this.pnlControls.Controls.Add(this.grpMetadata);
            this.pnlControls.Controls.Add(this.btnStart);
            this.pnlControls.Controls.Add(this.btnStop);
            this.pnlControls.Controls.Add(this.btnSave);
            this.pnlControls.Controls.Add(this.btnLoad);
            this.pnlControls.Controls.Add(this.lblVersion);
            
            // ===== CENTER PANEL (NOTE DISPLAY) =====
            this.pnlNoteDisplay = new System.Windows.Forms.Panel();
            this.pnlNoteDisplay.Location = new System.Drawing.Point(320, 10);
            this.pnlNoteDisplay.Size = new System.Drawing.Size(400, 230);
            this.pnlNoteDisplay.BackColor = System.Drawing.Color.Black;
            this.pnlNoteDisplay.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pnlNoteDisplay.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left;
            
            this.lblSelectedNote = new System.Windows.Forms.Label();
            this.lblSelectedNote.Text = "A4";
            this.lblSelectedNote.Location = new System.Drawing.Point(10, 10);
            this.lblSelectedNote.Size = new System.Drawing.Size(380, 50);
            this.lblSelectedNote.ForeColor = System.Drawing.Color.White;
            this.lblSelectedNote.Font = new System.Drawing.Font("Arial", 28F, System.Drawing.FontStyle.Bold);
            this.lblSelectedNote.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            
            this.lblFrequency = new System.Windows.Forms.Label();
            this.lblFrequency.Text = "440.00 Hz";
            this.lblFrequency.Location = new System.Drawing.Point(10, 70);
            this.lblFrequency.Size = new System.Drawing.Size(380, 35);
            this.lblFrequency.ForeColor = System.Drawing.Color.Cyan;
            this.lblFrequency.Font = new System.Drawing.Font("Arial", 20F, System.Drawing.FontStyle.Regular);
            this.lblFrequency.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            
            this.lblCents = new System.Windows.Forms.Label();
            this.lblCents.Text = "+0.0 cents";
            this.lblCents.Location = new System.Drawing.Point(10, 115);
            this.lblCents.Size = new System.Drawing.Size(380, 35);
            this.lblCents.ForeColor = System.Drawing.Color.Lime;
            this.lblCents.Font = new System.Drawing.Font("Arial", 20F, System.Drawing.FontStyle.Regular);
            this.lblCents.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            
            this.lblQuality = new System.Windows.Forms.Label();
            this.lblQuality.Text = "Quality: --";
            this.lblQuality.Location = new System.Drawing.Point(10, 180);
            this.lblQuality.Size = new System.Drawing.Size(380, 30);
            this.lblQuality.ForeColor = System.Drawing.Color.Yellow;
            this.lblQuality.Font = new System.Drawing.Font("Arial", 14F, System.Drawing.FontStyle.Regular);
            this.lblQuality.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            
            this.pnlNoteDisplay.Controls.Add(this.lblSelectedNote);
            this.pnlNoteDisplay.Controls.Add(this.lblFrequency);
            this.pnlNoteDisplay.Controls.Add(this.lblCents);
            this.pnlNoteDisplay.Controls.Add(this.lblQuality);
            
            // ===== VOLUME BAR =====
            this.pnlVolumeBar = new System.Windows.Forms.Panel();
            this.pnlVolumeBar.Location = new System.Drawing.Point(730, 10);
            this.pnlVolumeBar.Size = new System.Drawing.Size(650, 50);
            this.pnlVolumeBar.BackColor = System.Drawing.Color.Black;
            this.pnlVolumeBar.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pnlVolumeBar.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.pnlVolumeBar.Paint += new System.Windows.Forms.PaintEventHandler(this.pnlVolumeBar_Paint);
            
            // ===== SPECTRUM PANEL =====
            this.pnlSpectrum = new System.Windows.Forms.Panel();
            this.pnlSpectrum.Location = new System.Drawing.Point(320, 260);
            this.pnlSpectrum.Size = new System.Drawing.Size(1060, 420);
            this.pnlSpectrum.BackColor = System.Drawing.Color.Black;
            this.pnlSpectrum.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pnlSpectrum.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right | System.Windows.Forms.AnchorStyles.Bottom;
            this.pnlSpectrum.Paint += new System.Windows.Forms.PaintEventHandler(this.pnlSpectrum_Paint);
            typeof(Panel).GetProperty("DoubleBuffered", 
                System.Reflection.BindingFlags.Instance | 
                System.Reflection.BindingFlags.NonPublic)
                .SetValue(this.pnlSpectrum, true);
            
            // ===== PIANO KEYBOARD PANEL =====
            this.pnlPianoKeyboard = new System.Windows.Forms.Panel();
            this.pnlPianoKeyboard.Location = new System.Drawing.Point(320, 680);
            this.pnlPianoKeyboard.Size = new System.Drawing.Size(1060, 180);
            this.pnlPianoKeyboard.BackColor = System.Drawing.Color.White;
            this.pnlPianoKeyboard.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pnlPianoKeyboard.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.pnlPianoKeyboard.Paint += new System.Windows.Forms.PaintEventHandler(this.pnlPianoKeyboard_Paint);
            typeof(Panel).GetProperty("DoubleBuffered", 
                System.Reflection.BindingFlags.Instance | 
                System.Reflection.BindingFlags.NonPublic)
                .SetValue(this.pnlPianoKeyboard, true);
            
            // ===== ADD ALL TO FORM =====
            this.Controls.Add(this.pnlControls);
            this.Controls.Add(this.pnlNoteDisplay);
            this.Controls.Add(this.pnlVolumeBar);
            this.Controls.Add(this.pnlSpectrum);
            this.Controls.Add(this.pnlPianoKeyboard);
            
            this.ResumeLayout(false);
        }

        #endregion

        // ===== DECLARE CONTROLS =====
        private System.Windows.Forms.Panel pnlControls;
        private System.Windows.Forms.Label lblAsioDriver;
        private System.Windows.Forms.ComboBox cmbAsioDriver;
        private System.Windows.Forms.GroupBox grpMetadata;
        private System.Windows.Forms.Label lblPianoType;
        private System.Windows.Forms.ComboBox cmbPianoType;
        private System.Windows.Forms.Label lblCustomType;
        private System.Windows.Forms.TextBox txtPianoType;
        private System.Windows.Forms.Label lblPianoLength;
        private System.Windows.Forms.NumericUpDown numPianoLength;
        private System.Windows.Forms.Label lblScaleBreak;
        private System.Windows.Forms.NumericUpDown numScaleBreak;
        private System.Windows.Forms.Button btnStart;
        private System.Windows.Forms.Button btnStop;
        private System.Windows.Forms.Button btnSave;
        private System.Windows.Forms.Button btnLoad;
        private System.Windows.Forms.Label lblVersion;
        private System.Windows.Forms.Panel pnlNoteDisplay;
        private System.Windows.Forms.Label lblSelectedNote;
        private System.Windows.Forms.Label lblFrequency;
        private System.Windows.Forms.Label lblCents;
        private System.Windows.Forms.Label lblQuality;
        private System.Windows.Forms.Panel pnlVolumeBar;
        private System.Windows.Forms.Panel pnlSpectrum;
        private System.Windows.Forms.Panel pnlPianoKeyboard;
    }
}
