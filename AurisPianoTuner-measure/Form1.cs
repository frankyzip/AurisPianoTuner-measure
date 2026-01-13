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
        private double _displayVolumeDb = -60.0; // Smoothed level used for UI drawing
        private FftSpectrumData? _currentSpectrumData = null; // Current spectrum for visualization
        private NoteMeasurement? _currentMeasurement = null; // Current measurement for partial visualization

        private const double NoiseGateThresholdDb = -40.0; // Visual indicator for signal strength
        private const double VolumeSmoothingFactor = 0.18; // UI smoothing coefficient

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

            EnableDoubleBuffering(pnlVolumeBar);
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
            // Bereken RMS alleen voor de visuele volume bar
            double rms = CalculateRMS(samples);
            double db = 20 * Math.Log10(Math.Max(rms, 1e-6));
            _currentVolumeDb = db;
            _displayVolumeDb += (db - _displayVolumeDb) * VolumeSmoothingFactor;

            // UI update voor volume bar
            if (InvokeRequired)
                BeginInvoke(() => pnlVolumeBar.Invalidate());
            else
                pnlVolumeBar.Invalidate();

            // Stuur ALTIJD de samples naar de analyzer. 
            // De analyzer v2.1 beslist nu zelf (via AttackDetection) wanneer de meting start.
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
            // Update de lokale lijst
            _measurements[measurement.MidiIndex] = measurement;
            UpdateMeasurementDisplay(measurement);

            // Audio feedback (Ping!)
            System.Media.SystemSounds.Asterisk.Play();

            // We stoppen de audioService NIET. De analyzer v2.1 staat nu op "Locked".
            // De gebruiker kan nu rustig de resultaten bekijken.
            // Zodra de gebruiker een nieuwe noot aanklikt, wordt de lock opgeheven.

            lblQuality.Text = $"METING VOLTOOID: {measurement.Quality} - Klik op de volgende noot";
            pnlPianoKeyboard.Invalidate();
        }

        // ===== UI UPDATE METHODS =====
        private void UpdateMeasurementDisplay(NoteMeasurement measurement)
        {
            // Store current measurement for partial visualization
            _currentMeasurement = measurement;

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

            // Trigger spectrum repaint voor partial markers
            pnlSpectrum.Invalidate();
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
            double dbNormalized = Math.Max(0, Math.Min(1, (_displayVolumeDb - dbMin) / (dbMax - dbMin)));

            int barWidth = (int)(pnlVolumeBar.Width * dbNormalized);

            // Color coding based on level
            Brush barBrush;
            if (_displayVolumeDb > -10) // Too loud (clipping risk)
                barBrush = Brushes.Red;
            else if (_displayVolumeDb > -40) // Above gate = Good signal
                barBrush = Brushes.Lime;
            else if (_displayVolumeDb > -50) // Below gate but visible
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
            string dbText = $"{_displayVolumeDb:F1} dB";
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
            g.SmoothingMode = SmoothingMode.AntiAlias;

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

            // Calculate frequency range to display (full range for partials: f0/2 to 16*f0)
            double freqMin = targetFreq / 2.0;  // One octave below
            double freqMax = targetFreq * 18.0; // Cover up to 16th partial plus margin

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

            // Use logarithmic frequency scale for better partial spread
            double logFreqMin = Math.Log10(freqMin);
            double logFreqMax = Math.Log10(freqMax);
            double logFreqRange = logFreqMax - logFreqMin;

            // Draw spectrum with logarithmic frequency scale
            for (int i = binMin; i <= binMax; i++)
            {
                double freq = i * freqResolution;
                double logFreq = Math.Log10(freq);
                double normalizedLogFreq = (logFreq - logFreqMin) / logFreqRange;

                double mag = magnitudes[i];
                double normalizedMag = mag / maxMag;

                int barHeight = (int)(normalizedMag * pnlSpectrum.Height * 0.85);
                int x = (int)(normalizedLogFreq * pnlSpectrum.Width);
                int y = pnlSpectrum.Height - barHeight;

                // Color based on magnitude (blue spectrum)
                int intensity = (int)(normalizedMag * 200) + 55;
                Color barColor = Color.FromArgb(50, 100, intensity);

                using (SolidBrush brush = new SolidBrush(barColor))
                {
                    g.FillRectangle(brush, x, y, 2, barHeight);
                }
            }

            // Draw target frequency marker (fundamental)
            double targetLogFreq = Math.Log10(targetFreq);
            double normalizedTargetLogFreq = (targetLogFreq - logFreqMin) / logFreqRange;
            int targetX = (int)(normalizedTargetLogFreq * pnlSpectrum.Width);

            using (Pen markerPen = new Pen(Color.Yellow, 2))
            {
                g.DrawLine(markerPen, targetX, 0, targetX, pnlSpectrum.Height);
            }

            // Draw frequency label for target
            string targetLabel = $"{targetFreq:F1} Hz";
            using (Font font = new Font("Arial", 9, FontStyle.Bold))
            {
                g.DrawString(targetLabel, font, Brushes.Yellow, targetX + 3, 5);
            }

            // ===== DRAW DETECTED PARTIALS =====
            if (_currentMeasurement != null && _currentMeasurement.DetectedPartials.Count > 0)
            {
                // Find max amplitude for normalization
                double maxAmplitude = _currentMeasurement.DetectedPartials.Max(p => p.Amplitude);
                double minAmplitude = _currentMeasurement.DetectedPartials.Min(p => p.Amplitude);
                double amplitudeRange = maxAmplitude - minAmplitude;
                if (amplitudeRange < 1) amplitudeRange = 1;

                using (Font labelFont = new Font("Arial", 8, FontStyle.Bold))
                using (Font freqFont = new Font("Arial", 7, FontStyle.Regular))
                using (Pen partialPen = new Pen(Color.Lime, 2))
                using (Pen partialPenWeak = new Pen(Color.FromArgb(100, 150, 255), 2))
                {
                    foreach (var partial in _currentMeasurement.DetectedPartials.OrderBy(p => p.n))
                    {
                        double partialLogFreq = Math.Log10(partial.Frequency);
                        double normalizedPartialLogFreq = (partialLogFreq - logFreqMin) / logFreqRange;

                        // Skip if outside visible range
                        if (normalizedPartialLogFreq < 0 || normalizedPartialLogFreq > 1)
                            continue;

                        int partialX = (int)(normalizedPartialLogFreq * pnlSpectrum.Width);

                        // Calculate height based on amplitude (normalized to 0-1)
                        double normalizedAmplitude = (partial.Amplitude - minAmplitude) / amplitudeRange;
                        int barHeight = (int)(normalizedAmplitude * pnlSpectrum.Height * 0.75) + (int)(pnlSpectrum.Height * 0.1);
                        int barY = pnlSpectrum.Height - barHeight;

                        // Color: green for strong, blue for weak, red for fundamental
                        Color partialColor;
                        if (partial.n == 1)
                            partialColor = Color.Red;
                        else if (normalizedAmplitude > 0.5)
                            partialColor = Color.Lime;
                        else
                            partialColor = Color.FromArgb(100, 180, 255);

                        using (Pen pen = new Pen(partialColor, 3))
                        {
                            g.DrawLine(pen, partialX, barY, partialX, pnlSpectrum.Height);
                        }

                        // Draw partial number label at top
                        string nLabel = $"n={partial.n}";
                        SizeF nSize = g.MeasureString(nLabel, labelFont);
                        float labelX = partialX - nSize.Width / 2;
                        labelX = Math.Max(2, Math.Min(pnlSpectrum.Width - nSize.Width - 2, labelX));

                        using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
                        {
                            g.FillRectangle(bgBrush, labelX - 2, barY - nSize.Height - 2, nSize.Width + 4, nSize.Height + 2);
                        }
                        using (SolidBrush textBrush = new SolidBrush(partialColor))
                        {
                            g.DrawString(nLabel, labelFont, textBrush, labelX, barY - nSize.Height - 1);
                        }

                        // Draw frequency label at bottom
                        string freqLabel = $"{partial.Frequency:F0} Hz";
                        SizeF freqSize = g.MeasureString(freqLabel, freqFont);
                        float freqLabelX = partialX - freqSize.Width / 2;
                        freqLabelX = Math.Max(2, Math.Min(pnlSpectrum.Width - freqSize.Width - 2, freqLabelX));
                        float freqLabelY = pnlSpectrum.Height - freqSize.Height - 3;

                        using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
                        {
                            g.FillRectangle(bgBrush, freqLabelX - 2, freqLabelY - 1, freqSize.Width + 4, freqSize.Height + 2);
                        }
                        g.DrawString(freqLabel, freqFont, Brushes.White, freqLabelX, freqLabelY);
                    }
                }

                // Draw legend in top-right corner
                int legendX = pnlSpectrum.Width - 180;
                int legendY = 10;
                using (Font legendFont = new Font("Arial", 9, FontStyle.Regular))
                {
                    g.FillRectangle(new SolidBrush(Color.FromArgb(200, 0, 0, 0)), legendX - 5, legendY - 5, 175, 75);
                    g.DrawString($"Partials: {_currentMeasurement.DetectedPartials.Count}", legendFont, Brushes.White, legendX, legendY);
                    g.DrawString($"f₀: {_currentMeasurement.CalculatedFundamental:F2} Hz", legendFont, Brushes.Cyan, legendX, legendY + 18);
                    g.DrawString($"B: {_currentMeasurement.InharmonicityCoefficient:E2}", legendFont, Brushes.Yellow, legendX, legendY + 36);
                    g.DrawString($"Quality: {_currentMeasurement.Quality}", legendFont,
                        _currentMeasurement.Quality == "Groen" ? Brushes.Lime :
                        _currentMeasurement.Quality == "Oranje" ? Brushes.Orange : Brushes.Red,
                        legendX, legendY + 54);
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

                    // ===== AANPASSING IN DE LUS VAN pnlPianoKeyboard_Paint =====
                    // 1. Standaardkleur bepalen
                    Brush fillBrush = Brushes.White;

                    // 2. Kleur bepalen op basis van meting (indien aanwezig)
                    if (_measurements.TryGetValue(midiNote, out var m))
                    {
                        Color qualityColor = m.Quality switch
                        {
                            "Groen" => Color.FromArgb(150, 0, 255, 0),
                            "Oranje" => Color.FromArgb(150, 255, 165, 0),
                            "Rood" => Color.FromArgb(150, 255, 0, 0),
                            _ => Color.FromArgb(150, Color.Yellow)
                        };
                        fillBrush = new SolidBrush(qualityColor);
                    }

                    // 3. Speciale status voor de geselecteerde toets
                    if (_selectedKeyMidi == midiNote)
                    {
                        // Als we nog NIET klaar zijn met meten: toon blauw
                        if (!_fftAnalyzer.IsMeasurementLocked)
                        {
                            if (fillBrush != Brushes.White) fillBrush.Dispose();
                            fillBrush = new SolidBrush(Color.LightSkyBlue);
                        }
                        // Als we WEL klaar zijn (Locked): behoud de kwaliteitskleur
                        // maar teken er een dikke Cyaan rand omheen ter indicatie van selectie.
                        using (Pen selectionPen = new Pen(Color.Cyan, 3))
                        {
                            g.DrawRectangle(selectionPen, keyRect.X + 1, keyRect.Y + 1, keyRect.Width - 2, keyRect.Height - 2);
                        }
                    }

                    g.FillRectangle(fillBrush, keyRect);

                    // 3. Highlight de rand met Cyaan voor geselecteerde toets
                    if (_selectedKeyMidi == midiNote)
                    {
                        using (Pen selectionPen = new Pen(Color.Cyan, 3))
                        {
                            g.DrawRectangle(selectionPen, keyRect.X + 1, keyRect.Y + 1, keyRect.Width - 2, keyRect.Height - 2);
                        }
                    }

                    // Draw normal black border for all keys
                    g.DrawRectangle(Pens.Black, keyRect.X, keyRect.Y, keyRect.Width, keyRect.Height);

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

                    // ===== AANPASSING IN DE LUS VAN pnlPianoKeyboard_Paint =====
                    // 1. Standaardkleur bepalen
                    Brush fillBrush = Brushes.Black;

                    // 2. Kleur bepalen op basis van meting (indien aanwezig)
                    if (_measurements.TryGetValue(midiNote, out var m))
                    {
                        Color qualityColor = m.Quality switch
                        {
                            "Groen" => Color.FromArgb(150, 0, 255, 0),
                            "Oranje" => Color.FromArgb(150, 255, 165, 0),
                            "Rood" => Color.FromArgb(150, 255, 0, 0),
                            _ => Color.FromArgb(150, Color.Yellow)
                        };
                        fillBrush = new SolidBrush(qualityColor);
                    }

                    // 3. Speciale status voor de geselecteerde toets
                    if (_selectedKeyMidi == midiNote)
                    {
                        // Als we nog NIET klaar zijn met meten: toon blauw
                        if (!_fftAnalyzer.IsMeasurementLocked)
                        {
                            if (fillBrush != Brushes.Black) fillBrush.Dispose();
                            fillBrush = new SolidBrush(Color.LightSkyBlue);
                        }
                        // Als we WEL klaar zijn (Locked): behoud de kwaliteitskleur
                        // maar teken er een dikke Cyaan rand omheen ter indicatie van selectie.
                        using (Pen selectionPen = new Pen(Color.Cyan, 3))
                        {
                            g.DrawRectangle(selectionPen, keyRect.X + 1, keyRect.Y + 1, keyRect.Width - 2, keyRect.Height - 2);
                        }
                    }

                    g.FillRectangle(fillBrush, keyRect);

                    // 3. Highlight de rand met Cyaan voor geselecteerde toets
                    if (_selectedKeyMidi == midiNote)
                    {
                        using (Pen selectionPen = new Pen(Color.Cyan, 3))
                        {
                            g.DrawRectangle(selectionPen, keyRect.X + 1, keyRect.Y + 1, keyRect.Width - 2, keyRect.Height - 2);
                        }
                    }

                    // Draw normal black border for all keys
                    g.DrawRectangle(Pens.Black, keyRect.X, keyRect.Y, keyRect.Width, keyRect.Height);

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
                        // Show confirmation dialog before deleting
                        string noteName = PianoPhysics.MidiToNoteName(clickedMidi.Value);
                        string message = $"Weet je zeker dat je de gemeten data voor {noteName} wilt verwijderen?";
                        string caption = "Meting verwijderen";

                        DialogResult result = MessageBox.Show(message, caption,
                                                            MessageBoxButtons.YesNo,
                                                            MessageBoxIcon.Question);

                        if (result == DialogResult.Yes)
                        {
                            _measurements.Remove(clickedMidi.Value);
                            _keyColors.Remove(clickedMidi.Value);
                            pnlPianoKeyboard.Invalidate();

                            // Clear display if this was the selected key
                            if (_selectedKeyMidi == clickedMidi.Value)
                            {
                                lblQuality.Text = "Meting verwijderd";
                                lblQuality.ForeColor = Color.Gray;
                            }
                        }
                    }
                    return;
                }

                // Left click: select key and show details OR start measuring new note
                HandlePianoKeyClick(clickedMidi.Value);

                pnlPianoKeyboard.Invalidate();
            }
        }

        private void HandlePianoKeyClick(int midiNote)
        {
            // 1. CONTINUOUS WORKFLOW (The "Workflow" Fix)
            // If the audio engine is already running, we DO NOT stop it.
            // We only reset the analyzer for the new target.

            _selectedKeyMidi = midiNote;
            
            bool isAudioRunning = _audioService.IsRunning;

            // Update UI selection
            _currentTargetMidi = midiNote;
            double targetFreq = PianoPhysics.MidiToFrequency(midiNote);
            lblSelectedNote.Text = PianoPhysics.MidiToNoteName(midiNote);
            lblFrequency.Text = $"{targetFreq:F2} Hz (target)";
            lblCents.Text = "---";
            
            if (_measurements.TryGetValue(midiNote, out var measurement) && !isAudioRunning)
            {
                DisplayMeasurementDetails(measurement);
                return;
            }
            
            // Prepare Metadata
            UpdatePianoMetadata();

            if (isAudioRunning)
            {
                // FAST PATH: Just switch target, keep ASIO running
                _fftAnalyzer.SetTargetNote(midiNote, targetFreq);
                
                // Visual feedback that we switched
                lblQuality.Text = "Listening... Play note!"; 
                lblQuality.ForeColor = Color.Yellow;
                
                // Ensure UI is in recording state visually
                _isRecording = true;
                btnStart.Enabled = false;
                btnStop.Enabled = true;
                cmbAsioDriver.Enabled = false;
                cmbPianoType.Enabled = false;
            }
            else
            {
                // COLD START: User hasn't pressed Start yet, so we just select it.
                lblQuality.Text = "Ready to measure";
                lblQuality.ForeColor = Color.Yellow;
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

        private static void EnableDoubleBuffering(Control control)
        {
            if (control == null)
            {
                return;
            }

            var property = control.GetType().GetProperty(
                "DoubleBuffered",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);

            property?.SetValue(control, true, null);
        }
    }
}