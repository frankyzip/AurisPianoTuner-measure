using AurisPianoTuner_measure.Services;
using AurisPianoTuner_measure.Models;
using AurisPianoTuner_measure.Utils;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using static AurisPianoTuner_measure.Models.PianoMetadata;

namespace AurisPianoTuner_measure
{
    public partial class Form1 : Form
    {
        // ===== SERVICES =====
        private readonly AsioAudioService _audioService;
        private readonly FftAnalyzerService _fftAnalyzer;
        private readonly MeasurementStorageService _storageService;
        private readonly TestLoggerService _testLogger;
        
        // ===== STATE =====
        private Dictionary<int, NoteMeasurement> _measurements = new();
        private PianoMetadata _pianoMetadata = new();
        private int _currentTargetMidi = 69; // Start met A4
        private bool _isRecording = false;
        private double _currentVolumeDb = -60.0; // Current audio level in dB
        private FftSpectrumData? _currentSpectrumData = null; // Current spectrum for visualization

        // ADD THESE NEW FIELDS FOR NOISE GATING:
        private const double NoiseGateThresholdDb = -40.0; // Signaal moet boven -40 dB zijn
        private bool _signalDetected = false;
        private DateTime _lastSignalTime = DateTime.MinValue;
        private const double SignalTimeoutSeconds = 2.0; // Na 2 sec stilte: stop wachten

        // ===== KEYBOARD INTERACTION STATE =====
        private int? _selectedKeyMidi = null; // Currently selected key for measurement
        private Dictionary<int, Color> _keyColors = new(); // Color per measured key
        private Dictionary<int, RectangleF> _keyRectangles = new(); // Hit detection rectangles

        public Form1()
        {
            InitializeComponent();

            // Initialiseer services
            _audioService = new AsioAudioService();
            _fftAnalyzer = new FftAnalyzerService();
            _storageService = new MeasurementStorageService();
            _testLogger = new TestLoggerService();

            // Koppel events
            _audioService.AudioDataAvailable += OnAudioDataAvailable;
            _fftAnalyzer.MeasurementUpdated += OnMeasurementUpdated;
            _fftAnalyzer.RawSpectrumUpdated += OnRawSpectrumUpdated;
            _fftAnalyzer.MeasurementAutoStopped += OnMeasurementAutoStopped;

            // Wire testlogger aan analyzer
            _fftAnalyzer.TestLogger = _testLogger;

            // Vul ASIO dropdown
            LoadAsioDrivers();

            // Zet initial piano metadata
            UpdatePianoMetadata();

            // Window handlers
            this.Resize += Form1_Resize;
            this.FormClosing += Form1_FormClosing;

            // Keyboard interaction handlers
            pnlPianoKeyboard.MouseClick += PnlPianoKeyboard_MouseClick;
            pnlPianoKeyboard.MouseDown += PnlPianoKeyboard_MouseDown;
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_SYSCOMMAND = 0x0112;
            const int SC_CLOSE = 0xF060;
            const int SC_MINIMIZE = 0xF020;
            const int SC_MAXIMIZE = 0xF030;
            const int SC_RESTORE = 0xF120;

            if (m.Msg == WM_SYSCOMMAND)
            {
                int cmd = m.WParam.ToInt32() & 0xFFF0;

                switch (cmd)
                {
                    case SC_CLOSE:
                        // Let the normal closing sequence run
                        this.Close();
                        return;
                    case SC_MINIMIZE:
                        this.WindowState = FormWindowState.Minimized;
                        return;
                    case SC_MAXIMIZE:
                        this.WindowState = FormWindowState.Maximized;
                        return;
                    case SC_RESTORE:
                        this.WindowState = FormWindowState.Normal;
                        return;
                }
            }

            base.WndProc(ref m);
        }

        private void Form1_Resize(object? sender, EventArgs e)
        {
            // On minimize/maximize/restore, repaint dynamic panels so UI stays correct
            if (this.WindowState == FormWindowState.Minimized)
                return;

            pnlPianoKeyboard?.Invalidate();
            pnlSpectrum?.Invalidate();
            pnlVolumeBar?.Invalidate();
            pnlNoteDisplay?.Invalidate();
        }

        private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
        {
            // Stop ASIO audio als het nog draait
            if (_audioService.IsRunning)
            {
                _audioService.Stop();
            }
            
            // Dispose services
            _audioService.Dispose();
        }

        // ===== INITIALIZATION =====
        private void LoadAsioDrivers()
        {
            try
            {
                var drivers = _audioService.GetAsioDrivers().ToList();
                
                cmbAsioDriver.Items.Clear();
                
                if (drivers.Any())
                {
                    foreach (var driver in drivers)
                    {
                        cmbAsioDriver.Items.Add(driver);
                    }
                    cmbAsioDriver.SelectedIndex = 0;
                }
                else
                {
                    cmbAsioDriver.Items.Add("Geen ASIO drivers gevonden");
                    cmbAsioDriver.SelectedIndex = 0;
                    cmbAsioDriver.Enabled = false;
                    btnStart.Enabled = false;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fout bij laden ASIO drivers: {ex.Message}", 
                               "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdatePianoMetadata()
        {
            _pianoMetadata = new PianoMetadata
            {
                Type = GetPianoTypeFromSelection(),
                DimensionCm = (int)numPianoLength.Value,
                ScaleBreakMidiNote = (int)numScaleBreak.Value
            };
            
            _fftAnalyzer.SetPianoMetadata(_pianoMetadata);
        }

        private PianoType GetPianoTypeFromSelection()
        {
            return cmbPianoType.SelectedIndex switch
            {
                0 => PianoType.Spinet,
                1 => PianoType.Console,
                2 => PianoType.Console, // Studio
                3 => PianoType.ProfessionalUpright,
                4 => PianoType.BabyGrand,
                5 => PianoType.ParlorGrand,
                6 => PianoType.SemiConcertGrand,
                7 => PianoType.ConcertGrand,
                _ => PianoType.Unknown
            };
        }

        // ===== AUDIO PROCESSING EVENT HANDLERS =====
        private void OnAudioDataAvailable(object? sender, float[] samples)
        {
            // Update volume bar (calculate RMS)
            double rms = CalculateRMS(samples);
            double db = 20 * Math.Log10(Math.Max(rms, 1e-6));
            _currentVolumeDb = db;
            
            // Thread-safe UI update voor volume bar (altijd tonen)
            if (InvokeRequired)
            {
                BeginInvoke(() => pnlVolumeBar.Invalidate());
            }
            else
            {
                pnlVolumeBar.Invalidate();
            }
            
            // NOISE GATE: Alleen FFT analyse doen als signaal sterk genoeg is
            if (db < NoiseGateThresholdDb)
            {
                // Signaal te zwak - negeer
                if (_signalDetected)
                {
                    // Update UI: wacht op sterker signaal
                    if (InvokeRequired)
                    {
                        BeginInvoke(() => UpdateWaitingStatus("Signal too weak - play louder"));
                    }
                    else
                    {
                        UpdateWaitingStatus("Signal too weak - play louder");
                    }
                }
                return; // Skip FFT processing
            }
            
            // Sterk genoeg signaal gedetecteerd
            _signalDetected = true;
            _lastSignalTime = DateTime.Now;
            
            // Update UI: analyzing
            if (InvokeRequired)
            {
                BeginInvoke(() => UpdateWaitingStatus("Analyzing..."));
            }
            else
            {
                UpdateWaitingStatus("Analyzing...");
            }
            
            // Stuur naar FFT analyzer
            _fftAnalyzer.ProcessAudioBuffer(samples);
        }

        private void OnMeasurementUpdated(object? sender, NoteMeasurement measurement)
        {
            // Thread-safe UI update
            if (InvokeRequired)
            {
                BeginInvoke(() => UpdateMeasurementDisplay(measurement));
                return;
            }
            
            UpdateMeasurementDisplay(measurement);
        }

        private void OnRawSpectrumUpdated(object? sender, FftSpectrumData spectrumData)
        {
            // Update current spectrum data
            _currentSpectrumData = spectrumData;

            // Thread-safe UI update voor spectrum visualizer
            if (InvokeRequired)
            {
                BeginInvoke(() => UpdateSpectrumDisplay(spectrumData));
                return;
            }

            UpdateSpectrumDisplay(spectrumData);
        }

        private void OnMeasurementAutoStopped(object? sender, NoteMeasurement measurement)
        {
            // Thread-safe UI update
            if (InvokeRequired)
            {
                BeginInvoke(() => HandleAutoStop(measurement));
                return;
            }

            HandleAutoStop(measurement);
        }

        private void HandleAutoStop(NoteMeasurement measurement)
        {
            // Update display with final measurement
            UpdateMeasurementDisplay(measurement);

            // Auto-stop the recording
            try
            {
                _audioService.Stop();
                _fftAnalyzer.Reset();
                _isRecording = false;

                btnStart.Enabled = true;
                btnStop.Enabled = false;
                cmbAsioDriver.Enabled = true;

                // Re-enable metadata editing
                cmbPianoType.Enabled = true;
                numPianoLength.Enabled = true;
                numScaleBreak.Enabled = true;

                // Update quality label
                lblQuality.Text = $"Auto-stopped: {measurement.Quality} ({measurement.DetectedPartials.Count} partials) ✓";
                lblQuality.ForeColor = measurement.Quality switch
                {
                    "Groen" => Color.Lime,
                    "Oranje" => Color.Orange,
                    "Rood" => Color.Red,
                    _ => Color.Yellow
                };

                System.Diagnostics.Debug.WriteLine($"[Auto-Stop] Measurement completed and stopped for {measurement.NoteName}");
            }
            catch (Exception ex)
            {
                lblQuality.Text = $"Auto-stop error: {ex.Message}";
                lblQuality.ForeColor = Color.Red;
            }
        }

        // ===== UI UPDATE METHODS =====
        private void UpdateMeasurementDisplay(NoteMeasurement measurement)
        {
            lblSelectedNote.Text = measurement.NoteName;
            lblFrequency.Text = $"{measurement.CalculatedFundamental:F2} Hz";

            // Calculate cents deviation from target frequency
            double centsDeviation = 1200 * Math.Log2(measurement.CalculatedFundamental / measurement.TargetFrequency);
            lblCents.Text = $"{centsDeviation:+0.0;-0.0} cents";

            // ADD: Show number of detected partials (quality indicator)
            int partialCount = measurement.DetectedPartials.Count;
            lblQuality.Text = $"Quality: {measurement.Quality} ({partialCount} partials)";

            // Color coding
            Color qualityColor = measurement.Quality switch
            {
                "Groen" => Color.Lime,
                "Oranje" => Color.Orange,
                "Rood" => Color.Red,
                _ => Color.Yellow
            };
            lblQuality.ForeColor = qualityColor;

            // Opslaan in measurements dictionary
            _measurements[measurement.MidiIndex] = measurement;

            // Update key color voor visuele feedback op keyboard
            _keyColors[measurement.MidiIndex] = qualityColor;

            // Trigger piano keyboard repaint (voor kleur indicaties)
            pnlPianoKeyboard.Invalidate();
        }

        private void UpdateVolumeBar(double db)
        {
            // Force repaint van volume bar panel
            pnlVolumeBar.Invalidate();
        }

        private void UpdateSpectrumDisplay(FftSpectrumData spectrumData)
        {
            // Force repaint van spectrum panel
            pnlSpectrum.Invalidate();
        }

        private void UpdateWaitingStatus(string status)
        {
            // Update quality label om feedback te geven tijdens wachten
            if (_isRecording && lblQuality.Text == "Measuring...")
            {
                lblQuality.Text = status;
                lblQuality.ForeColor = Color.Yellow;
            }
        }

        // ===== HELPER METHODS =====
        private double CalculateRMS(float[] samples)
        {
            double sum = 0;
            foreach (var sample in samples)
            {
                sum += sample * sample;
            }
            return Math.Sqrt(sum / samples.Length);
        }

        // ===== EVENT HANDLERS (CONTROLS) =====
        private void cmbPianoType_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Update the piano length based on selected piano type
            switch (cmbPianoType.SelectedIndex)
            {
                case 0: // Spinet
                    numPianoLength.Value = 95;
                    break;
                case 1: // Console
                    numPianoLength.Value = 107;
                    break;
                case 2: // Studio
                    numPianoLength.Value = 115;
                    break;
                case 3: // Professional Upright
                    numPianoLength.Value = 131;
                    break;
                case 4: // Baby Grand
                    numPianoLength.Value = 149;
                    break;
                case 5: // Parlor Grand
                    numPianoLength.Value = 180;
                    break;
                case 6: // Semi-Concert Grand
                    numPianoLength.Value = 215;
                    break;
                case 7: // Concert Grand
                    numPianoLength.Value = 274;
                    break;
            }
        }

        // ===== EVENT HANDLERS (BUTTONS) =====
        private void btnStart_Click(object sender, EventArgs e)
        {
            try
            {
                // Check if a key is selected
                if (!_selectedKeyMidi.HasValue)
                {
                    lblQuality.Text = "Please select a key first!";
                    lblQuality.ForeColor = Color.Red;
                    return;
                }

                // Update metadata
                UpdatePianoMetadata();

                // Start ASIO audio
                string selectedDriver = cmbAsioDriver.SelectedItem?.ToString() ?? string.Empty;

                if (string.IsNullOrEmpty(selectedDriver) || selectedDriver.Contains("Geen"))
                {
                    lblQuality.Text = "Select valid ASIO driver!";
                    lblQuality.ForeColor = Color.Red;
                    return;
                }

                _audioService.Start(selectedDriver, 96000);

                // Use selected key as target
                _currentTargetMidi = _selectedKeyMidi.Value;
                double targetFreq = PianoPhysics.MidiToFrequency(_currentTargetMidi);
                _fftAnalyzer.SetTargetNote(_currentTargetMidi, targetFreq);

                // Update UI
                lblSelectedNote.Text = PianoPhysics.MidiToNoteName(_currentTargetMidi);
                lblFrequency.Text = $"{targetFreq:F2} Hz (target)";
                lblCents.Text = "---";
                lblQuality.Text = "Measuring... Play note loudly!";
                lblQuality.ForeColor = Color.Yellow;

                _isRecording = true;
                btnStart.Enabled = false;
                btnStop.Enabled = true;
                cmbAsioDriver.Enabled = false;

                // Disable metadata editing during measurement
                cmbPianoType.Enabled = false;
                numPianoLength.Enabled = false;
                numScaleBreak.Enabled = false;

                // NO MORE DIALOG - immediate feedback
            }
            catch (NotSupportedException ex)
            {
                lblQuality.Text = $"ASIO Error: {ex.Message}";
                lblQuality.ForeColor = Color.Red;
            }
            catch (Exception ex)
            {
                lblQuality.Text = $"Start error: {ex.Message}";
                lblQuality.ForeColor = Color.Red;

                // Reset UI state
                btnStart.Enabled = true;
                btnStop.Enabled = false;
                cmbAsioDriver.Enabled = true;
            }
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            try
            {
                _audioService.Stop();
                _fftAnalyzer.Reset();
                _isRecording = false;

                btnStart.Enabled = true;
                btnStop.Enabled = false;
                cmbAsioDriver.Enabled = true;

                // Re-enable metadata editing
                cmbPianoType.Enabled = true;
                numPianoLength.Enabled = true;
                numScaleBreak.Enabled = true;

                lblQuality.Text = "Stopped - Select another key to measure";
                lblQuality.ForeColor = Color.Gray;

                // NO MORE DIALOG - direct feedback
            }
            catch (Exception ex)
            {
                lblQuality.Text = $"Stop error: {ex.Message}";
                lblQuality.ForeColor = Color.Red;
            }
        }

        private async void btnSave_Click(object sender, EventArgs e)
        {
            try
            {
                if (_measurements.Count == 0)
                {
                    MessageBox.Show("Geen metingen om op te slaan", 
                                   "Waarschuwing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                
                using var saveDialog = new SaveFileDialog
                {
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    DefaultExt = "json",
                    FileName = $"Measurements_{_pianoMetadata.Type}_{DateTime.Now:yyyyMMdd_HHmm}.json"
                };
                
                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    await _storageService.SaveMeasurementsAsync(
                        saveDialog.FileName, 
                        _measurements, 
                        _pianoMetadata);
                    
                    // Save test log
                    await _testLogger.SaveSessionLogAsync(_pianoMetadata.Type.ToString());
                    
                    MessageBox.Show($"Metingen opgeslagen naar:\n{saveDialog.FileName}", 
                                   "Opgeslagen", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fout bij opslaan: {ex.Message}", 
                               "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void btnLoad_Click(object sender, EventArgs e)
        {
            try
            {
                using var openDialog = new OpenFileDialog
                {
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    DefaultExt = "json"
                };
                
                if (openDialog.ShowDialog() == DialogResult.OK)
                {
                    var (measurements, metadata) = await _storageService.LoadMeasurementsAsync(openDialog.FileName);
                    
                    _measurements = measurements;
                    
                    if (metadata != null)
                    {
                        _pianoMetadata = metadata;
                        
                        // Update UI met geladen metadata
                        // txtPianoType.Text = metadata.Type; // Als u txtPianoType gebruikt
                        numPianoLength.Value = metadata.DimensionCm;
                        numScaleBreak.Value = metadata.ScaleBreakMidiNote;
                    }
                    
                    // Repaint piano keyboard met loaded data
                    pnlPianoKeyboard.Invalidate();
                    
                    MessageBox.Show($"Metingen geladen:\n{measurements.Count} noten\n\nPiano: {metadata?.Type}", 
                                   "Geladen", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fout bij laden: {ex.Message}", 
                               "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ===== PAINT EVENTS =====
        private void pnlVolumeBar_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Color.Black);

            // Convert dB to percentage (range: -60 dB to 0 dB)
            double dbMin = -60.0;
            double dbMax = 0.0;
            double dbNormalized = Math.Max(0, Math.Min(1, (_currentVolumeDb - dbMin) / (dbMax - dbMin)));
            
            int barWidth = (int)(pnlVolumeBar.Width * dbNormalized);
            
            // Color coding based on level
            Brush barBrush;
            if (_currentVolumeDb > -10) // Too loud (clipping risk)
                barBrush = Brushes.Red;
            else if (_currentVolumeDb > NoiseGateThresholdDb) // Above gate = Good signal
                barBrush = Brushes.Lime;
            else if (_currentVolumeDb > -50) // Below gate but visible
                barBrush = Brushes.DarkOrange;
            else // Too quiet
                barBrush = Brushes.DarkGray;
            
            // Draw volume bar
            if (barWidth > 0)
            {
                g.FillRectangle(barBrush, 0, 0, barWidth, pnlVolumeBar.Height);
            }
            
            // Draw NOISE GATE THRESHOLD line (visual indicator)
            double gateNormalized = (NoiseGateThresholdDb - dbMin) / (dbMax - dbMin);
            int gateX = (int)(pnlVolumeBar.Width * gateNormalized);
            using (Pen gatePen = new Pen(Color.Yellow, 2))
            {
                gatePen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                g.DrawLine(gatePen, gateX, 0, gateX, pnlVolumeBar.Height);
            }
            
            // Draw grid lines (dB markers)
            using (Pen gridPen = new Pen(Color.FromArgb(80, 80, 80)))
            {
                for (int i = 0; i <= 10; i++)
                {
                    int x = i * (pnlVolumeBar.Width / 10);
                    g.DrawLine(gridPen, x, 0, x, pnlVolumeBar.Height);
                }
            }
            
            // Draw dB value text
            string dbText = $"{_currentVolumeDb:F1} dB";
            using (Font font = new Font("Arial", 10, FontStyle.Bold))
            {
                SizeF textSize = g.MeasureString(dbText, font);
                float textX = pnlVolumeBar.Width - textSize.Width - 5;
                float textY = (pnlVolumeBar.Height - textSize.Height) / 2;
                
                // Draw text with black outline for readability
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddString(dbText, font.FontFamily, (int)font.Style, font.Size,
                                  new PointF(textX, textY), StringFormat.GenericDefault);
                    
                    g.DrawPath(new Pen(Color.Black, 3), path);
                    g.FillPath(Brushes.White, path);
                }
            }
            
            // Draw "GATE" label at threshold
            using (Font smallFont = new Font("Arial", 8, FontStyle.Bold))
            {
                g.DrawString("GATE", smallFont, Brushes.Yellow, gateX + 3, 3);
            }
        }

        private void pnlSpectrum_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Color.Black);

            if (_currentSpectrumData == null || _currentSpectrumData.Magnitudes.Length == 0)
            {
                // Draw placeholder grid when no data
                using (Pen gridPen = new Pen(Color.FromArgb(50, 50, 50)))
                {
                    for (int i = 0; i <= 10; i++)
                    {
                        int y = i * (pnlSpectrum.Height / 10);
                        g.DrawLine(gridPen, 0, y, pnlSpectrum.Width, y);
                    }
                }
                
                // Draw "Waiting for audio..." text
                string waitText = _isRecording ? "Waiting for audio signal..." : "Press START to begin";
                using (Font font = new Font("Arial", 14, FontStyle.Bold))
                {
                    SizeF textSize = g.MeasureString(waitText, font);
                    float x = (pnlSpectrum.Width - textSize.Width) / 2;
                    float y = (pnlSpectrum.Height - textSize.Height) / 2;
                    g.DrawString(waitText, font, Brushes.Gray, x, y);
                }
                return;
            }

            // Draw spectrum data
            double[] magnitudes = _currentSpectrumData.Magnitudes;
            double freqResolution = _currentSpectrumData.FrequencyResolution;
            double targetFreq = _currentSpectrumData.TargetFrequency;
            
            // Calculate frequency range to display (3 octaves around target)
            double freqMin = targetFreq / 2.0;  // One octave below
            double freqMax = targetFreq * 4.0;  // Two octaves above
            
            int binMin = (int)(freqMin / freqResolution);
            int binMax = (int)(freqMax / freqResolution);
            binMin = Math.Max(0, binMin);
            binMax = Math.Min(magnitudes.Length - 1, binMax);
            
            if (binMax <= binMin) return;
            
            // Find max magnitude in visible range for scaling
            double maxMag = 0;
            for (int i = binMin; i <= binMax; i++)
            {
                if (magnitudes[i] > maxMag) maxMag = magnitudes[i];
            }
            
            if (maxMag < 1e-10) return; // No signal
            
            // Draw spectrum bars
            int visibleBins = binMax - binMin + 1;
            float barWidth = (float)pnlSpectrum.Width / visibleBins;
            
            for (int i = binMin; i <= binMax; i++)
            {
                double mag = magnitudes[i];
                double normalizedMag = mag / maxMag;
                
                int barHeight = (int)(normalizedMag * pnlSpectrum.Height * 0.9);
                int x = (int)((i - binMin) * barWidth);
                int y = pnlSpectrum.Height - barHeight;
                
                // Color based on magnitude
                Color barColor;
                if (normalizedMag > 0.7)
                    barColor = Color.FromArgb(255, 50, 50); // Red for peaks
                else if (normalizedMag > 0.3)
                    barColor = Color.FromArgb(50, 255, 50); // Green for medium
                else
                    barColor = Color.FromArgb(50, 150, 255); // Blue for low
                
                using (SolidBrush brush = new SolidBrush(barColor))
                {
                    g.FillRectangle(brush, x, y, Math.Max(1, barWidth - 1), barHeight);
                }
            }
            
            // Draw target frequency marker
            int targetBin = (int)(targetFreq / freqResolution);
            if (targetBin >= binMin && targetBin <= binMax)
            {
                int targetX = (int)((targetBin - binMin) * barWidth);
                using (Pen markerPen = new Pen(Color.Yellow, 2))
                {
                    g.DrawLine(markerPen, targetX, 0, targetX, pnlSpectrum.Height);
                }
                
                // Draw frequency label
                string freqLabel = $"{targetFreq:F1} Hz";
                using (Font font = new Font("Arial", 9, FontStyle.Bold))
                {
                    g.DrawString(freqLabel, font, Brushes.Yellow, targetX + 3, 5);
                }
            }
        }

        private void pnlPianoKeyboard_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.White);

            // Piano: 88 toetsen (MIDI 21-108, A0 tot C8)
            int totalKeys = 88;
            int startMidi = 21; // A0
            float whiteKeyWidth = pnlPianoKeyboard.Width / 52.0f; // 52 witte toetsen
            float whiteKeyHeight = pnlPianoKeyboard.Height;
            float blackKeyWidth = whiteKeyWidth * 0.6f;
            float blackKeyHeight = whiteKeyHeight * 0.65f;

            // STAP 1: Teken alle WITTE toetsen met nootnamen
            int whiteKeyIndex = 0;
            for (int i = 0; i < totalKeys; i++)
            {
                int midiNote = startMidi + i;
                bool isWhiteKey = IsWhiteKey(midiNote);

                if (isWhiteKey)
                {
                    float x = whiteKeyIndex * whiteKeyWidth;
                    RectangleF keyRect = new RectangleF(x, 0, whiteKeyWidth - 1, whiteKeyHeight);

                    // Determine key fill color
                    Brush fillBrush = Brushes.White;
                    if (_keyColors.TryGetValue(midiNote, out Color measurementColor))
                    {
                        // Key has been measured - use measurement quality color
                        fillBrush = new SolidBrush(Color.FromArgb(100, measurementColor)); // Semi-transparent
                    }

                    g.FillRectangle(fillBrush, keyRect);

                    // Draw border (selected key gets blue border)
                    if (_selectedKeyMidi == midiNote)
                    {
                        using (Pen selectedPen = new Pen(Color.Blue, 3))
                        {
                            g.DrawRectangle(selectedPen, keyRect.X, keyRect.Y, keyRect.Width, keyRect.Height);
                        }
                    }
                    else
                    {
                        g.DrawRectangle(Pens.Black, keyRect.X, keyRect.Y, keyRect.Width, keyRect.Height);
                    }

                    // Cleanup brush if created
                    if (fillBrush != Brushes.White)
                    {
                        fillBrush.Dispose();
                    }

                    // Voeg nootnaam toe op de witte toets
                    string noteName = GetNoteName(midiNote);

                    // Bepaal fontgrootte gebaseerd op toetsbreedte
                    float fontSize = Math.Max(8f, whiteKeyWidth * 0.15f);
                    using (Font font = new Font("Arial", fontSize, FontStyle.Bold))
                    {
                        using (Brush textBrush = new SolidBrush(Color.Black))
                        {
                            // Positioneer tekst onderaan de toets
                            SizeF textSize = g.MeasureString(noteName, font);
                            float textX = x + (whiteKeyWidth - textSize.Width) / 2;
                            float textY = whiteKeyHeight - textSize.Height - 5;

                            g.DrawString(noteName, font, textBrush, textX, textY);
                        }
                    }

                    whiteKeyIndex++;
                }
            }

            // STAP 2: Teken alle ZWARTE toetsen (bovenop)
            whiteKeyIndex = 0;
            for (int i = 0; i < totalKeys; i++)
            {
                int midiNote = startMidi + i;
                bool isWhiteKey = IsWhiteKey(midiNote);

                if (isWhiteKey)
                {
                    whiteKeyIndex++;
                }
                else // Zwarte toets
                {
                    float x = (whiteKeyIndex * whiteKeyWidth) - (blackKeyWidth / 2);
                    RectangleF keyRect = new RectangleF(x, 0, blackKeyWidth, blackKeyHeight);

                    // Determine fill color for black keys
                    Brush fillBrush = Brushes.Black;
                    if (_keyColors.TryGetValue(midiNote, out Color measurementColor))
                    {
                        // Black key has been measured - darker semi-transparent color
                        fillBrush = new SolidBrush(Color.FromArgb(150, measurementColor));
                    }

                    g.FillRectangle(fillBrush, keyRect);

                    // Draw border (selected key gets blue border)
                    if (_selectedKeyMidi == midiNote)
                    {
                        using (Pen selectedPen = new Pen(Color.Blue, 3))
                        {
                            g.DrawRectangle(selectedPen, keyRect.X, keyRect.Y, keyRect.Width, keyRect.Height);
                        }
                    }
                    else
                    {
                        g.DrawRectangle(Pens.Black, keyRect.X, keyRect.Y, keyRect.Width, keyRect.Height);
                    }

                    // Cleanup brush if created
                    if (fillBrush != Brushes.Black)
                    {
                        fillBrush.Dispose();
                    }
                }
            }
        }

        // ===== HELPER METHODS =====
        private bool IsWhiteKey(int midiNote)
        {
            // MIDI note modulo 12 geeft positie binnen octaaf (0=C, 1=C#, 2=D, ...)
            int noteInOctave = midiNote % 12;

            // Witte toetsen: C(0), D(2), E(4), F(5), G(7), A(9), B(11)
            return noteInOctave == 0 || noteInOctave == 2 || noteInOctave == 4 ||
                   noteInOctave == 5 || noteInOctave == 7 || noteInOctave == 9 ||
                   noteInOctave == 11;
        }

        private string GetNoteName(int midiNote)
        {
            // MIDI noot naar nootnaam en octaaf converteren
            string[] noteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

            int noteInOctave = midiNote % 12;
            int octave = (midiNote / 12) - 1; // MIDI octaaf correctie

            return noteNames[noteInOctave] + octave.ToString();
        }

        // ===== KEYBOARD MOUSE INTERACTION =====
        private void PnlPianoKeyboard_MouseClick(object? sender, MouseEventArgs e)
        {
            // Find which key was clicked
            int? clickedMidi = GetKeyAtPosition(e.X, e.Y);

            if (clickedMidi.HasValue)
            {
                // Right click: delete measurement
                if (e.Button == MouseButtons.Right)
                {
                    if (_measurements.ContainsKey(clickedMidi.Value))
                    {
                        _measurements.Remove(clickedMidi.Value);
                        _keyColors.Remove(clickedMidi.Value);
                        pnlPianoKeyboard.Invalidate();

                        // Clear display if this was the selected key
                        if (_selectedKeyMidi == clickedMidi.Value)
                        {
                            lblQuality.Text = "Measurement deleted";
                            lblQuality.ForeColor = Color.Gray;
                        }
                    }
                    return;
                }

                // Left click: select key and show details
                _selectedKeyMidi = clickedMidi.Value;
                _currentTargetMidi = clickedMidi.Value;

                // Show details if measurement exists
                if (_measurements.TryGetValue(clickedMidi.Value, out var measurement))
                {
                    DisplayMeasurementDetails(measurement);
                }
                else
                {
                    // Show selected note info
                    double targetFreq = PianoPhysics.MidiToFrequency(clickedMidi.Value);
                    lblSelectedNote.Text = PianoPhysics.MidiToNoteName(clickedMidi.Value);
                    lblFrequency.Text = $"{targetFreq:F2} Hz (target)";
                    lblCents.Text = "---";
                    lblQuality.Text = "Ready to measure";
                    lblQuality.ForeColor = Color.Yellow;
                }

                pnlPianoKeyboard.Invalidate();
            }
        }

        private void PnlPianoKeyboard_MouseDown(object? sender, MouseEventArgs e)
        {
            // Handle mouse down for potential drag operations (future feature)
        }

        private int? GetKeyAtPosition(float x, float y)
        {
            // Piano: 88 keys (MIDI 21-108, A0 to C8)
            int totalKeys = 88;
            int startMidi = 21;
            float whiteKeyWidth = pnlPianoKeyboard.Width / 52.0f;
            float whiteKeyHeight = pnlPianoKeyboard.Height;
            float blackKeyWidth = whiteKeyWidth * 0.6f;
            float blackKeyHeight = whiteKeyHeight * 0.65f;

            // Check black keys first (they're on top)
            int whiteKeyIndex = 0;
            for (int i = 0; i < totalKeys; i++)
            {
                int midiNote = startMidi + i;
                bool isWhiteKey = IsWhiteKey(midiNote);

                if (isWhiteKey)
                {
                    whiteKeyIndex++;
                }
                else // Black key
                {
                    float keyX = (whiteKeyIndex * whiteKeyWidth) - (blackKeyWidth / 2);
                    RectangleF keyRect = new RectangleF(keyX, 0, blackKeyWidth, blackKeyHeight);

                    if (keyRect.Contains(x, y))
                    {
                        return midiNote;
                    }
                }
            }

            // Check white keys
            whiteKeyIndex = 0;
            for (int i = 0; i < totalKeys; i++)
            {
                int midiNote = startMidi + i;
                bool isWhiteKey = IsWhiteKey(midiNote);

                if (isWhiteKey)
                {
                    float keyX = whiteKeyIndex * whiteKeyWidth;
                    RectangleF keyRect = new RectangleF(keyX, 0, whiteKeyWidth - 1, whiteKeyHeight);

                    if (keyRect.Contains(x, y))
                    {
                        return midiNote;
                    }

                    whiteKeyIndex++;
                }
            }

            return null; // No key clicked
        }

        private void DisplayMeasurementDetails(NoteMeasurement measurement)
        {
            lblSelectedNote.Text = measurement.NoteName;
            lblFrequency.Text = $"{measurement.CalculatedFundamental:F2} Hz";

            double centsDeviation = 1200 * Math.Log2(measurement.CalculatedFundamental / measurement.TargetFrequency);
            lblCents.Text = $"{centsDeviation:+0.0;-0.0} cents";

            int partialCount = measurement.DetectedPartials.Count;
            lblQuality.Text = $"{measurement.Quality} ({partialCount} partials) - Measured at {measurement.MeasuredAt:HH:mm:ss}";

            lblQuality.ForeColor = measurement.Quality switch
            {
                "Groen" => Color.Lime,
                "Oranje" => Color.Orange,
                "Rood" => Color.Red,
                _ => Color.Yellow
            };
        }
    }
}