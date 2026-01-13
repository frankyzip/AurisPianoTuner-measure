using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AurisPianoTuner_measure.Models;
using AurisPianoTuner_measure.Utils;
using MathNet.Numerics.IntegralTransforms;

namespace AurisPianoTuner_measure.Services
{
    public class FftAnalyzerService : IFftAnalyzerService
    {
        // ============================================================
        // ADAPTIVE FFT CONFIGURATION (v2.0)
        // Scientific basis: Treble notes decay faster (<100ms) than bass
        // Using shorter FFT for treble improves SNR before decay
        // ============================================================
        private const int MaxFftSize = 32768;       // 2^15 for bass (341ms @ 96kHz)
        private const int MediumFftSize = 16384;    // 2^14 for mid-range (170ms @ 96kHz)
        private const int SmallFftSize = 8192;      // 2^13 for treble (85ms @ 96kHz)
        private const int ZeroPaddedFftSize = 32768; // Always use 32k for output resolution
        
        private const int SampleRate = 96000;
        
        // Pre-computed windows for each FFT size
        private readonly double[] _windowLarge;
        private readonly double[] _windowMedium;
        private readonly double[] _windowSmall;
        
        // Audio buffer sized for maximum FFT
        private readonly float[] _audioBuffer = new float[MaxFftSize];
        private int _bufferWritePos = 0;

        private int _targetMidi;
        private double _targetFreq;
        private bool _hasTarget = false;
        private PianoMetadata? _pianoMetadata;

        // Rolling buffer for best measurement selection
        private readonly List<NoteMeasurement> _measurementBuffer = new();
        private const int MaxBufferSize = 10;
        private NoteMeasurement? _bestMeasurement = null;

        // ===== NEW: State Management for Automatic Measurement (v2.1) =====
        private bool _isMeasuring = false;
        private bool _measurementLocked = false;
        private double _previousRms = -100.0;
        private int _consecutiveGoodMeasurements = 0;
        private const int AutoStopThreshold = 3;        // Number of stable measurements before locking
        private const double AttackThresholdDb = 15.0;  // Required delta for trigger
        private const double NoiseGateDb = -45.0;       // Minimum absolute level

        // Frequency filtering: ±50 cents window
        private double _freqMin = 0;
        private double _freqMax = 0;

        public event EventHandler<NoteMeasurement>? MeasurementUpdated;
        public event EventHandler<NoteMeasurement>? MeasurementAutoStopped;
        public event EventHandler<FftSpectrumData>? RawSpectrumUpdated;

        public ITestLoggerService? TestLogger { get; set; }

        /// <summary>
        /// Geeft aan of de huidige meting "locked" is (stabiele meting bereikt).
        /// </summary>
        public bool IsMeasurementLocked => _measurementLocked;

        // Noise floor reference region
        private const double NoiseRefStartHz = 100.0;
        private const double NoiseRefEndHz = 500.0;

        // Multi-frame averaging
        private const int AveragingFrameCount = 3;
        private readonly Queue<double[]> _magnitudeFrameBuffer = new();
        private const int MinFramesBeforeAnalysis = 2;

        // Adaptive B-based search windows
        private readonly Queue<double> _bCoefficientHistory = new();
        private const int BHistorySize = 5;
        private double _smoothedB = 0.0001;

        // Minimum reliable magnitude for parabolic interpolation
        private const double MinReliableMagnitude = 1e-6;

        // ============================================================
        // REGISTER-BASED B HEURISTICS (v2.0)
        // Scientific basis: Fletcher & Rossing (1998), Conklin (1996)
        // Used when regression fails or produces negative B
        // ============================================================
        private static readonly Dictionary<int, (double minB, double typicalB, double maxB)> RegisterBRanges = new()
        {
            // Deep Bass (A0-B1, MIDI 21-35): Wound strings, highest inharmonicity
            { 21, (0.0003, 0.0008, 0.003) },
            { 35, (0.0003, 0.0008, 0.003) },
            
            // Bass (C2-B2, MIDI 36-47): Transition zone often here
            { 36, (0.0002, 0.0005, 0.001) },
            { 47, (0.0002, 0.0005, 0.001) },
            
            // Tenor (C3-C4, MIDI 48-60): Mixed, near scale break
            { 48, (0.0001, 0.0003, 0.0006) },
            { 60, (0.0001, 0.0003, 0.0006) },
            
            // Mid-High (C#4-C5, MIDI 61-72): Plain strings, moderate B
            { 61, (0.00005, 0.00015, 0.0003) },
            { 72, (0.00005, 0.00015, 0.0003) },
            
            // Treble (C#5-C6, MIDI 73-84): Plain strings, lower B
            { 73, (0.00003, 0.0001, 0.0002) },
            { 84, (0.00003, 0.0001, 0.0002) },
            
            // High Treble (C#6-C8, MIDI 85-108): Shortest strings, increasing B
            { 85, (0.00005, 0.00015, 0.0004) },
            { 108, (0.0001, 0.0003, 0.001) }
        };

        public FftAnalyzerService()
        {
            // Initialize Blackman-Harris windows for each FFT size
            _windowLarge = CreateBlackmanHarrisWindow(MaxFftSize);
            _windowMedium = CreateBlackmanHarrisWindow(MediumFftSize);
            _windowSmall = CreateBlackmanHarrisWindow(SmallFftSize);
        }

        /// <summary>
        /// Creates a Blackman-Harris window for optimal spectral resolution.
        /// Scientific basis: Harris (1978) - "On the Use of Windows for Harmonic Analysis"
        /// </summary>
        private static double[] CreateBlackmanHarrisWindow(int size)
        {
            double[] window = new double[size];
            for (int i = 0; i < size; i++)
            {
                window[i] = 0.35875
                          - 0.48829 * Math.Cos(2 * Math.PI * i / (size - 1))
                          + 0.14128 * Math.Cos(4 * Math.PI * i / (size - 1))
                          - 0.01168 * Math.Cos(6 * Math.PI * i / (size - 1));
            }
            return window;
        }

        /// <summary>
        /// Selects optimal FFT size based on MIDI note register.
        /// </summary>
        private (int fftSize, double[] window) GetAdaptiveFftParameters(int midiNote)
        {
            return midiNote switch
            {
                >= 79 => (SmallFftSize, _windowSmall),    // G5+ (784 Hz+): 85ms window
                >= 72 => (MediumFftSize, _windowMedium),  // C5-F#5: 170ms window
                _ => (MaxFftSize, _windowLarge)            // Below C5: 341ms window
            };
        }

        public void SetPianoMetadata(PianoMetadata metadata)
        {
            _pianoMetadata = metadata;
            System.Diagnostics.Debug.WriteLine($"[FftAnalyzer] Piano metadata set: {metadata.Type}, {metadata.DimensionCm}cm, Scale Break: {PianoPhysics.MidiToNoteName(metadata.ScaleBreakMidiNote)}");
        }

        public void SetTargetNote(int midiIndex, double theoreticalFrequency)
        {
            _targetMidi = midiIndex;
            _targetFreq = theoreticalFrequency;
            _bufferWritePos = 0;
            _hasTarget = true;

            _freqMin = theoreticalFrequency * Math.Pow(2, -50.0 / 1200.0);
            _freqMax = theoreticalFrequency * Math.Pow(2, 50.0 / 1200.0);

            // Reset state for the new note
            _measurementLocked = false;
            _isMeasuring = false;
            _consecutiveGoodMeasurements = 0;
            _measurementBuffer.Clear();
            _bestMeasurement = null;
            _previousRms = -100.0; // Reset RMS baseline
            _magnitudeFrameBuffer.Clear();
            _bCoefficientHistory.Clear();
            _smoothedB = GetHeuristicB(_targetMidi); // Initialize with register-appropriate B

            System.Diagnostics.Debug.WriteLine($"[Target] Note set to {PianoPhysics.MidiToNoteName(midiIndex)} ({theoreticalFrequency:F2} Hz). Waiting for attack...");
        }

        public void Reset()
        {
            _bufferWritePos = 0;
            _hasTarget = false;
            Array.Clear(_audioBuffer, 0, _audioBuffer.Length);
            _magnitudeFrameBuffer.Clear();
            _bCoefficientHistory.Clear();
            _smoothedB = 0.0001;
            _isMeasuring = false;
            _measurementLocked = false;
            _previousRms = -100.0;
            _consecutiveGoodMeasurements = 0;
        }

        public void ProcessAudioBuffer(float[] samples)
        {
            if (!_hasTarget || _measurementLocked) return;

            // 1. Calculate RMS of the current block
            double currentRms = CalculateRms(samples);
            double deltaRms = currentRms - _previousRms;
            _previousRms = currentRms;

            // 2. Attack Detection: Trigger measurement on sudden volume spike
            if (!_isMeasuring && deltaRms > AttackThresholdDb && currentRms > NoiseGateDb)
            {
                _isMeasuring = true;
                _consecutiveGoodMeasurements = 0;
                _measurementBuffer.Clear();
                System.Diagnostics.Debug.WriteLine($"[Trigger] Attack detected for MIDI {_targetMidi} at {currentRms:F1} dB (Delta: {deltaRms:F1} dB)");
            }

            // 3. Process buffer if measuring
            var (fftSize, _) = GetAdaptiveFftParameters(_targetMidi);
            
            foreach (var sample in samples)
            {
                _audioBuffer[_bufferWritePos++] = sample;

                if (_bufferWritePos >= fftSize)
                {
                    if (_isMeasuring)
                    {
                        Analyze();
                    }

                    // 50% Overlap for continuous spectral monitoring
                    int half = fftSize / 2;
                    Array.Copy(_audioBuffer, half, _audioBuffer, 0, half);
                    _bufferWritePos = half;
                }
            }
        }

        private void Analyze()
        {
            // Perform full spectral analysis (v2.0 logic)
            var measurement = PerformFullAnalysis();

            if (measurement != null)
            {
                // Add to rolling buffer
                _measurementBuffer.Add(measurement);
                if (_measurementBuffer.Count > MaxBufferSize) _measurementBuffer.RemoveAt(0);

                // Update best measurement
                UpdateBestMeasurement();

                // Update UI in real-time
                MeasurementUpdated?.Invoke(this, _bestMeasurement ?? measurement);

                // Stability Check
                if (measurement.Quality == "Groen")
                {
                    _consecutiveGoodMeasurements++;
                }
                else
                {
                    _consecutiveGoodMeasurements = 0;
                }

                // 4. Auto-Stop: Lock when we have stable measurements
                if (_consecutiveGoodMeasurements >= AutoStopThreshold && _bestMeasurement != null)
                {
                    _isMeasuring = false;
                    _measurementLocked = true;

                    // Final update to notify UI that we are "Locked"
                    MeasurementAutoStopped?.Invoke(this, _bestMeasurement);
                    System.Diagnostics.Debug.WriteLine($"[Auto-Stop] Measurement locked for MIDI {_targetMidi}");
                }
            }
        }

        private NoteMeasurement? PerformFullAnalysis()
        {
            var (actualFftSize, window) = GetAdaptiveFftParameters(_targetMidi);
            
            Complex[] fftBuffer = new Complex[ZeroPaddedFftSize];
            
            for (int i = 0; i < actualFftSize; i++)
            {
                fftBuffer[i] = new Complex(_audioBuffer[i] * window[i], 0);
            }

            if (_magnitudeFrameBuffer.Count == 0)
            {
                double windowMs = 1000.0 * actualFftSize / SampleRate;
                double binResolution = (double)SampleRate / ZeroPaddedFftSize;
                System.Diagnostics.Debug.WriteLine(
                    $"[Adaptive FFT] MIDI {_targetMidi} ({PianoPhysics.MidiToNoteName(_targetMidi)}): " +
                    $"window={actualFftSize} samples ({windowMs:F1}ms), " +
                    $"zero-padded to {ZeroPaddedFftSize}, " +
                    $"bin resolution={binResolution:F2} Hz");
            }

            Fourier.Forward(fftBuffer, FourierOptions.NoScaling);

            int spectrumSize = ZeroPaddedFftSize / 2;
            double[] currentMagnitudes = new double[spectrumSize];
            for (int i = 0; i < spectrumSize; i++)
            {
                currentMagnitudes[i] = fftBuffer[i].Magnitude;
            }

            double[] averagedMagnitudes = AverageMagnitudeFrames(currentMagnitudes);

            for (int i = 0; i < spectrumSize; i++)
            {
                double originalPhase = fftBuffer[i].Phase;
                fftBuffer[i] = Complex.FromPolarCoordinates(averagedMagnitudes[i], originalPhase);
            }

            EmitRawSpectrumData(fftBuffer);

            var result = new NoteMeasurement
            {
                MidiIndex = _targetMidi,
                TargetFrequency = _targetFreq,
                NoteName = PianoPhysics.MidiToNoteName(_targetMidi)
            };

            bool isNearScaleBreak = false;
            ScaleBreakRegion scaleBreakRegion = ScaleBreakRegion.None;

            if (_pianoMetadata != null)
            {
                int scaleBreak = _pianoMetadata.ScaleBreakMidiNote;
                int distance = _targetMidi - scaleBreak;

                if (Math.Abs(distance) <= 3)
                {
                    isNearScaleBreak = true;
                    scaleBreakRegion = distance < -1 ? ScaleBreakRegion.WoundStrings :
                                       distance > 1 ? ScaleBreakRegion.PlainStrings :
                                       ScaleBreakRegion.Transition;
                }
            }

            var initialPartials = DetectPartialsPass1(fftBuffer, _targetFreq, _targetMidi, isNearScaleBreak);

            if (initialPartials.Count == 0)
            {
                result.Quality = "Rood";
                result.MeasuredPartialNumber = 1;
                result.CalculatedFundamental = 0;
                result.InharmonicityCoefficient = 0;
                result.DetectedPartials = new List<PartialResult>();
                return FilterAndLogResult(result, isNearScaleBreak, scaleBreakRegion);
            }

            double initialF0 = EstimateInitialF0(initialPartials, _targetFreq);
            double preliminaryB = CalculateInharmonicityCoefficientRobust(initialPartials, initialF0, scaleBreakRegion, _targetMidi);

            var refinedPartials = DetectPartialsPass2(fftBuffer, initialF0, preliminaryB, _targetMidi, isNearScaleBreak);
            var detectedPartials = refinedPartials.Count >= initialPartials.Count ? refinedPartials : initialPartials;

            result.DetectedPartials = detectedPartials;

            double currentF0 = initialF0;
            double currentB = preliminaryB;
            const int maxIterations = 5;
            const double convergenceThreshold = 0.01;

            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                double previousF0 = currentF0;

                currentB = CalculateInharmonicityCoefficientRobust(detectedPartials, currentF0, scaleBreakRegion, _targetMidi);

                var measuredPartial = SelectBestPartialForMeasurement(detectedPartials, _targetMidi);

                if (measuredPartial != null)
                {
                    double n = measuredPartial.n;
                    double fn = measuredPartial.Frequency;
                    double inharmonicityFactor = Math.Sqrt(1 + currentB * n * n);
                    currentF0 = fn / (n * inharmonicityFactor);
                }

                double delta = Math.Abs(currentF0 - previousF0);

                if (iteration > 0)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"  [Convergence] Iteration {iteration}: f0={currentF0:F2} Hz, " +
                        $"B={currentB:E4}, ?f0={delta:F4} Hz");
                }

                if (delta < convergenceThreshold)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"  [Convergence] Converged in {iteration + 1} iterations");
                    break;
                }
            }

            result.CalculatedFundamental = currentF0;
            result.InharmonicityCoefficient = currentB;

            UpdateSmoothedB(currentB);

            var finalMeasuredPartial = SelectBestPartialForMeasurement(detectedPartials, _targetMidi);
            result.MeasuredPartialNumber = finalMeasuredPartial?.n ?? 1;

            if (isNearScaleBreak && scaleBreakRegion == ScaleBreakRegion.Transition)
            {
                result.Quality = detectedPartials.Count > 7 ? "Groen" :
                               detectedPartials.Count > 4 ? "Oranje" : "Rood";
            }
            else
            {
                result.Quality = detectedPartials.Count > 5 ? "Groen" :
                               detectedPartials.Count > 2 ? "Oranje" : "Rood";
            }

            return FilterAndLogResult(result, isNearScaleBreak, scaleBreakRegion);
        }

        private NoteMeasurement? FilterAndLogResult(NoteMeasurement result, bool isNearScaleBreak, ScaleBreakRegion region)
        {
            if (result.CalculatedFundamental < _freqMin || result.CalculatedFundamental > _freqMax)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Freq Filter] REJECTED: f0={result.CalculatedFundamental:F2} Hz outside window " +
                    $"[{_freqMin:F2} - {_freqMax:F2} Hz]");
                return null;
            }

            if (_pianoMetadata != null)
            {
                string scaleBreakInfo = isNearScaleBreak ? $" [SCALE BREAK: {region}]" : "";
                System.Diagnostics.Debug.WriteLine(
                    $"[{_pianoMetadata.Type}] {result.NoteName} (MIDI {_targetMidi}): " +
                    $"{result.DetectedPartials.Count} partials, " +
                    $"f0={result.CalculatedFundamental:F2} Hz, " +
                    $"B={result.InharmonicityCoefficient:E4}{scaleBreakInfo}");
            }

            return result;
        }

        private List<PartialResult> DetectPartialsPass1(Complex[] fftData, double targetFreq, int midiIndex, bool isNearScaleBreak)
        {
            var detectedPartials = new List<PartialResult>();
            
            int maxPartial = midiIndex switch
            {
                >= 84 => 8,
                >= 72 => 12,
                >= 60 => 14,
                _ => 16
            };

            for (int n = 1; n <= maxPartial; n++)
            {
                double searchCenterFreq = targetFreq * n;
                
                if (searchCenterFreq > SampleRate / 2 - 1000) break;

                var partial = FindPrecisePeakPass1(fftData, searchCenterFreq, n, midiIndex, isNearScaleBreak);
                if (partial != null)
                {
                    detectedPartials.Add(partial);
                }
            }

            return detectedPartials;
        }

        private List<PartialResult> DetectPartialsPass2(Complex[] fftData, double f0, double B, int midiIndex, bool isNearScaleBreak)
        {
            var detectedPartials = new List<PartialResult>();

            int maxPartial = midiIndex switch
            {
                >= 84 => 8,
                >= 72 => 12,
                >= 60 => 14,
                _ => 16
            };

            for (int n = 1; n <= maxPartial; n++)
            {
                double expectedFreq = n * f0 * Math.Sqrt(1 + B * n * n);

                if (expectedFreq > SampleRate / 2 - 1000) break;

                var partial = FindPrecisePeakPass2(fftData, expectedFreq, n, midiIndex, isNearScaleBreak, B);
                if (partial != null)
                {
                    detectedPartials.Add(partial);
                }
            }

            System.Diagnostics.Debug.WriteLine(
                $"  [Pass 2] Detected {detectedPartials.Count} partials with B={B:E4} correction");

            return detectedPartials;
        }

        private double EstimateInitialF0(List<PartialResult> partials, double targetFreq)
        {
            var strongPartials = partials
                .Where(p => p.Amplitude > -40 && p.n >= 1 && p.n <= 8)
                .ToList();

            if (strongPartials.Count >= 2)
            {
                double weightedSum = 0;
                double totalWeight = 0;

                foreach (var p in strongPartials)
                {
                    double weight = 1.0 / p.n;
                    weightedSum += (p.Frequency / p.n) * weight;
                    totalWeight += weight;
                }

                return weightedSum / totalWeight;
            }
            else if (strongPartials.Count == 1)
            {
                return strongPartials[0].Frequency / strongPartials[0].n;
            }

            return targetFreq;
        }

        private double CalculateInharmonicityCoefficientRobust(
            List<PartialResult> partials,
            double estimatedF0,
            ScaleBreakRegion region,
            int midiIndex)
        {
            if (region == ScaleBreakRegion.Transition)
            {
                return CalculateBTransitionZone(partials, estimatedF0, midiIndex);
            }

            var validPartials = partials
                .Where(p => p.Amplitude > -50 && p.n >= 2 && p.n <= 12)
                .ToList();

            if (validPartials.Count < 3)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"  [B-Robust] Insufficient partials ({validPartials.Count}), using heuristic");
                return GetHeuristicB(midiIndex);
            }

            double sumW = 0, sumWX = 0, sumWY = 0, sumWXY = 0, sumWXX = 0;
            int validCount = 0;
            var deviations = new List<(int n, double deviation)>();

            foreach (var p in validPartials)
            {
                double n = p.n;
                double fn = p.Frequency;
                double x = n * n;
                double ratio = fn / (n * estimatedF0);
                double y = ratio * ratio - 1.0;

                deviations.Add((p.n, y));

                double maxY = midiIndex >= 72 ? 0.3 : 0.5;
                if (y < -0.05 || y > maxY)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"  [B-Robust] Outlier: n={p.n}, y={y:F4} (limit [-0.05, {maxY:F2}])");
                    continue;
                }

                double w = 1.0 / (n * n);
                
                sumW += w;
                sumWX += w * x;
                sumWY += w * y;
                sumWXY += w * x * y;
                sumWXX += w * x * x;
                validCount++;
            }

            if (validCount < 2)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"  [B-Robust] Insufficient valid partials after outlier removal");
                return GetHeuristicB(midiIndex);
            }

            double denominator = sumW * sumWXX - sumWX * sumWX;

            if (Math.Abs(denominator) < 1e-10)
            {
                return GetHeuristicB(midiIndex);
            }

            double B = (sumW * sumWXY - sumWX * sumWY) / denominator;

            if (B < 0)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"  [B-Robust] Negative B={B:E4} detected - analyzing slope");
                
                B = AnalyzeDeviationSlope(deviations, midiIndex, estimatedF0);
            }

            var (minB, typicalB, maxB) = GetBRangeForMidi(midiIndex);
            
            if (B < minB)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"  [B-Robust] B={B:E4} below minimum {minB:E4}, using minimum");
                B = minB;
            }
            else if (B > maxB)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"  [B-Robust] B={B:E4} above maximum {maxB:E4}, clamping");
                B = maxB;
            }

            if (region != ScaleBreakRegion.None)
            {
                string warning = ValidateBCoefficientForRegion(B, region);
                if (!string.IsNullOrEmpty(warning))
                {
                    System.Diagnostics.Debug.WriteLine($"  [B-Robust WARNING] {warning}");
                }
            }

            System.Diagnostics.Debug.WriteLine(
                $"  [B-Robust] f0={estimatedF0:F2} Hz, B={B:E4}, using {validCount} partials");

            return B;
        }

        private double AnalyzeDeviationSlope(List<(int n, double deviation)> deviations, int midiIndex, double f0)
        {
            if (deviations.Count < 3)
            {
                return GetHeuristicB(midiIndex);
            }

            deviations = deviations.OrderBy(d => d.n).ToList();

            int positiveCount = deviations.Count(d => d.deviation > 0);
            int negativeCount = deviations.Count(d => d.deviation < 0);

            if (negativeCount > positiveCount)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"  [Slope Analysis] {negativeCount}/{deviations.Count} partials appear flat - " +
                    $"likely noise floor issue, using heuristic");
                return GetHeuristicB(midiIndex);
            }

            var positiveDeviations = deviations.Where(d => d.deviation > 0).ToList();
            if (positiveDeviations.Count >= 2)
            {
                var sortedDevs = positiveDeviations.OrderBy(d => d.deviation).ToList();
                var median = sortedDevs[sortedDevs.Count / 2];
                
                double estimatedB = median.deviation / (median.n * median.n);
                
                var (minB, _, maxB) = GetBRangeForMidi(midiIndex);
                estimatedB = Math.Max(minB, Math.Min(maxB, estimatedB));
                
                System.Diagnostics.Debug.WriteLine(
                    $"  [Slope Analysis] Estimated B={estimatedB:E4} from median deviation");
                return estimatedB;
            }

            return GetHeuristicB(midiIndex);
        }

        private double CalculateBTransitionZone(List<PartialResult> partials, double estimatedF0, int midiIndex)
        {
            System.Diagnostics.Debug.WriteLine($"  [B-Calc] TRANSITION ZONE - using special handling");

            var lowPartials = partials
                .Where(p => p.Amplitude > -50 && p.n >= 2 && p.n <= 5)
                .ToList();

            if (lowPartials.Count >= 3)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"  [B-Calc] Using {lowPartials.Count} low partials (n=2-5)");

                double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;
                int count = 0;

                foreach (var p in lowPartials)
                {
                    double n = p.n;
                    double fn = p.Frequency;
                    double x = n * n;
                    double ratio = fn / (n * estimatedF0);
                    double y = ratio * ratio - 1.0;

                    if (y < -0.1 || y > 0.8) continue;

                    sumX += x;
                    sumY += y;
                    sumXY += x * y;
                    sumXX += x * x;
                    count++;
                }

                if (count >= 2)
                {
                    double denom = count * sumXX - sumX * sumX;
                    if (Math.Abs(denom) > 1e-10)
                    {
                        double B = (count * sumXY - sumX * sumY) / denom;
                        B = Math.Max(0.00001, Math.Min(0.01, B));
                        
                        System.Diagnostics.Debug.WriteLine(
                            $"  [B-Calc] TRANSITION: B={B:E4} from {count} low partials");
                        return B;
                    }
                }
            }

            if (_pianoMetadata != null)
            {
                int scaleBreak = _pianoMetadata.ScaleBreakMidiNote;
                double conservativeB = _targetMidi < scaleBreak ? 0.0006 : 0.0002;
                System.Diagnostics.Debug.WriteLine(
                    $"  [B-Calc] TRANSITION fallback: B={conservativeB:E4}");
                return conservativeB;
            }

            return GetHeuristicB(midiIndex);
        }

        private double GetHeuristicB(int midiIndex)
        {
            var (_, typicalB, _) = GetBRangeForMidi(midiIndex);
            return typicalB;
        }

        private (double minB, double typicalB, double maxB) GetBRangeForMidi(int midiIndex)
        {
            midiIndex = Math.Max(21, Math.Min(108, midiIndex));

            int[] boundaries = { 21, 35, 36, 47, 48, 60, 61, 72, 73, 84, 85, 108 };
            
            for (int i = 0; i < boundaries.Length - 1; i += 2)
            {
                int low = boundaries[i];
                int high = boundaries[i + 1];
                
                if (midiIndex >= low && midiIndex <= high)
                {
                    return RegisterBRanges[low];
                }
            }

            return (0.0001, 0.0003, 0.0006);
        }

        private PartialResult? FindPrecisePeakPass1(Complex[] fftData, double targetFreq, int n, int midiIndex, bool isNearScaleBreak)
        {
            return FindPrecisePeak(fftData, targetFreq, n, midiIndex, isNearScaleBreak, null);
        }

        private PartialResult? FindPrecisePeakPass2(Complex[] fftData, double expectedFreq, int n, int midiIndex, bool isNearScaleBreak, double B)
        {
            return FindPrecisePeak(fftData, expectedFreq, n, midiIndex, isNearScaleBreak, B);
        }

        private PartialResult? FindPrecisePeak(Complex[] fftData, double targetFreq, int n, int midiIndex, bool isNearScaleBreak, double? knownB)
        {
            const int MinSearchBins = 3;

            double binFreq = (double)SampleRate / ZeroPaddedFftSize;
            int centerBin = (int)(targetFreq / binFreq);

            double baseSearchCents = midiIndex switch
            {
                >= 84 => 10.0,
                >= 72 => 12.0,
                >= 60 => 15.0,
                >= 48 => 20.0,
                >= 36 => 25.0,
                _ => 30.0
            };

            if (knownB.HasValue && knownB.Value > 0)
            {
                baseSearchCents *= 0.7;
            }

            if (isNearScaleBreak)
            {
                baseSearchCents *= 1.4;
            }

            double partialExpansionFactor = 1.0 + (n - 1) * 0.1;
            double effectiveSearchCents = baseSearchCents * partialExpansionFactor;

            double referenceB = 0.0002;
            double bScaleFactor = Math.Sqrt(_smoothedB / referenceB);
            bScaleFactor = Math.Max(0.7, Math.Min(2.0, bScaleFactor));
            effectiveSearchCents *= bScaleFactor;

            effectiveSearchCents = Math.Min(effectiveSearchCents, 100.0);

            double searchWindowHz = targetFreq * (Math.Pow(2, effectiveSearchCents / 1200.0) - 1);

            double minSearchHz = targetFreq switch
            {
                < 50 => 2.0,
                < 100 => 3.0,
                < 200 => 4.0,
                _ => 0.0
            };

            double effectiveSearchHz = Math.Max(searchWindowHz, minSearchHz);
            int searchRange = Math.Max(MinSearchBins, (int)(effectiveSearchHz / binFreq));

            int minBin = Math.Max(1, centerBin - searchRange);
            int maxBin = Math.Min(ZeroPaddedFftSize / 2 - 2, centerBin + searchRange);

            int bestBin = -1;
            double maxMag = -1;

            for (int i = minBin; i <= maxBin; i++)
            {
                double mag = fftData[i].Magnitude;
                if (mag > maxMag) { maxMag = mag; bestBin = i; }
            }

            double baseNoiseThreshold = (targetFreq, n) switch
            {
                (< 60, _) => 0.003,
                (< 100, >= 6) => 0.002,
                (< 100, _) => 0.0015,
                (< 500, >= 4) => 0.0012,
                (< 1000, _) => 0.001,
                (>= 2000, _) => 0.0004,
                (_, >= 8) => 0.0008,
                _ => 0.0006
            };

            double estimatedNoiseFloor = EstimateNoiseFloor(fftData, centerBin, searchRange);
            double snrRequirement = 3.0;
            double snrBasedThreshold = estimatedNoiseFloor * snrRequirement;
            double adaptiveThreshold = Math.Max(snrBasedThreshold, baseNoiseThreshold);

            if (isNearScaleBreak) adaptiveThreshold *= 1.2;

            if (maxMag < adaptiveThreshold || bestBin < 1 || bestBin >= ZeroPaddedFftSize / 2 - 1)
            {
                return null;
            }

            double magPrev = fftData[bestBin - 1].Magnitude;
            double magPeak = fftData[bestBin].Magnitude;
            double magNext = fftData[bestBin + 1].Magnitude;

            if (magPrev < MinReliableMagnitude || magNext < MinReliableMagnitude)
            {
                double centerFreq = bestBin * binFreq;
                return new PartialResult
                {
                    n = n,
                    Frequency = centerFreq,
                    Amplitude = 20 * Math.Log10(maxMag)
                };
            }

            double maxNeighbor = Math.Max(magPrev, magNext);
            if (magPeak < 1.15 * maxNeighbor)
            {
                return null;
            }

            double magPrevSafe = Math.Max(magPrev, MinReliableMagnitude);
            double magPeakSafe = Math.Max(magPeak, MinReliableMagnitude);
            double magNextSafe = Math.Max(magNext, MinReliableMagnitude);

            double y1 = Math.Log(magPrevSafe);
            double y2 = Math.Log(magPeakSafe);
            double y3 = Math.Log(magNextSafe);

            double denominator = y1 - 2 * y2 + y3;

            if (Math.Abs(denominator) < 1e-10)
            {
                return new PartialResult
                {
                    n = n,
                    Frequency = bestBin * binFreq,
                    Amplitude = 20 * Math.Log10(maxMag)
                };
            }

            double d = (y1 - y3) / (2 * denominator);

            if (Math.Abs(d) > 1.0)
            {
                return new PartialResult
                {
                    n = n,
                    Frequency = bestBin * binFreq,
                    Amplitude = 20 * Math.Log10(maxMag)
                };
            }

            double preciseFreq = (bestBin + d) * binFreq;

            double actualDeviation = Math.Abs(preciseFreq - targetFreq);
            if (actualDeviation > searchWindowHz * 1.5)
            {
                return null;
            }

            double maxAllowedCents = n <= 4 ? 50.0 : (isNearScaleBreak ? 120.0 : 80.0);
            double actualCents = 1200 * Math.Log2(preciseFreq / targetFreq);
            if (Math.Abs(actualCents) > maxAllowedCents)
            {
                return null;
            }

            return new PartialResult
            {
                n = n,
                Frequency = preciseFreq,
                Amplitude = 20 * Math.Log10(maxMag)
            };
        }

        private string ValidateBCoefficientForRegion(double B, ScaleBreakRegion region)
        {
            return region switch
            {
                ScaleBreakRegion.WoundStrings when B < 0.0003 =>
                    $"Low B ({B:E3}) for wound strings (expected 300-1000×10??)",
                ScaleBreakRegion.WoundStrings when B > 0.001 =>
                    $"High B ({B:E3}) for wound strings (expected < 1000×10??)",
                ScaleBreakRegion.PlainStrings when B < 0.00005 =>
                    $"Very low B ({B:E3}) for plain strings (expected 50-400×10??)",
                ScaleBreakRegion.PlainStrings when B > 0.0005 =>
                    $"High B ({B:E3}) for plain strings (expected < 500×10??)",
                ScaleBreakRegion.Transition =>
                    $"Transition zone: B={B:E3}",
                _ => string.Empty
            };
        }

        private int GetOptimalPartialForRegister(int midiIndex)
        {
            return midiIndex switch
            {
                <= 35 => 6,
                <= 47 => 3,
                <= 60 => 2,
                _ => 1
            };
        }

        private PartialResult? SelectBestPartialForMeasurement(List<PartialResult> partials, int midiIndex)
        {
            if (partials == null || partials.Count == 0) return null;

            int optimalN = GetOptimalPartialForRegister(midiIndex);
            var optimalPartial = partials.FirstOrDefault(p => p.n == optimalN);

            if (optimalPartial != null && optimalPartial.Amplitude > -60)
            {
                return optimalPartial;
            }

            var acceptablePartials = midiIndex switch
            {
                <= 35 => partials.Where(p => p.n >= 4 && p.n <= 8),
                <= 47 => partials.Where(p => p.n >= 2 && p.n <= 4),
                <= 60 => partials.Where(p => p.n >= 1 && p.n <= 3),
                _ => partials.Where(p => p.n == 1)
            };

            return acceptablePartials.OrderByDescending(p => p.Amplitude).FirstOrDefault();
        }

        private double EstimateNoiseFloor(Complex[] fftData, int centerBin, int searchRange)
        {
            var noiseSamples = new List<double>();

            int exclusionZone = (int)(2.5 * searchRange);
            int sampleStart = Math.Max(1, centerBin - 4 * searchRange);
            int sampleEnd = Math.Min(fftData.Length / 2 - 1, centerBin + 4 * searchRange);

            for (int i = sampleStart; i < sampleEnd; i += Math.Max(1, searchRange / 2))
            {
                if (Math.Abs(i - centerBin) > exclusionZone)
                {
                    noiseSamples.Add(fftData[i].Magnitude);
                }
            }

            if (noiseSamples.Count >= 5)
            {
                return CalculateMedianNoiseFloor(noiseSamples);
            }

            noiseSamples.Clear();
            int belowSampleStart = 1;
            int belowSampleEnd = Math.Max(1, centerBin - exclusionZone);
            int belowStepSize = Math.Max(1, exclusionZone / 8);

            for (int i = belowSampleStart; i < belowSampleEnd && noiseSamples.Count < 30; i += belowStepSize)
            {
                noiseSamples.Add(fftData[i].Magnitude);
            }

            if (noiseSamples.Count >= 5)
            {
                return CalculateMedianNoiseFloor(noiseSamples);
            }

            noiseSamples.Clear();
            int refStartBin = (int)(NoiseRefStartHz / ((double)SampleRate / ZeroPaddedFftSize));
            int refEndBin = (int)(NoiseRefEndHz / ((double)SampleRate / ZeroPaddedFftSize));

            if (centerBin > refEndBin + exclusionZone)
            {
                for (int i = refStartBin; i < refEndBin && noiseSamples.Count < 30; i += 3)
                {
                    noiseSamples.Add(fftData[i].Magnitude);
                }

                if (noiseSamples.Count >= 5)
                {
                    return CalculateMedianNoiseFloor(noiseSamples);
                }
            }

            return 0.0001;
        }

        private double CalculateMedianNoiseFloor(List<double> noiseSamples)
        {
            if (noiseSamples.Count == 0) return 0.0001;

            noiseSamples.Sort();
            double medianNoiseFloor = noiseSamples[noiseSamples.Count / 2];
            return Math.Max(1e-6, Math.Min(0.01, medianNoiseFloor));
        }

        private double[] AverageMagnitudeFrames(double[] currentMagnitudes)
        {
            _magnitudeFrameBuffer.Enqueue(currentMagnitudes);

            while (_magnitudeFrameBuffer.Count > AveragingFrameCount)
            {
                _magnitudeFrameBuffer.Dequeue();
            }

            if (_magnitudeFrameBuffer.Count < MinFramesBeforeAnalysis)
            {
                return currentMagnitudes;
            }

            int spectrumSize = currentMagnitudes.Length;
            double[] averagedMagnitudes = new double[spectrumSize];
            int frameCount = _magnitudeFrameBuffer.Count;

            foreach (var frame in _magnitudeFrameBuffer)
            {
                for (int i = 0; i < spectrumSize; i++)
                {
                    averagedMagnitudes[i] += frame[i];
                }
            }

            for (int i = 0; i < spectrumSize; i++)
            {
                averagedMagnitudes[i] /= frameCount;
            }

            return averagedMagnitudes;
        }

        private void UpdateSmoothedB(double newB)
        {
            if (newB < 0.00001 || newB > 0.01) return;

            _bCoefficientHistory.Enqueue(newB);

            while (_bCoefficientHistory.Count > BHistorySize)
            {
                _bCoefficientHistory.Dequeue();
            }

            double sum = 0;
            int count = 0;
            foreach (var b in _bCoefficientHistory)
            {
                sum += b;
                count++;
            }

            _smoothedB = count > 0 ? sum / count : 0.0001;
        }

        private void UpdateBestMeasurement()
        {
            if (_measurementBuffer.Count == 0)
            {
                _bestMeasurement = null;
                return;
            }

            var sorted = _measurementBuffer
                .OrderByDescending(m => GetQualityScore(m))
                .ThenByDescending(m => m.DetectedPartials.Count)
                .ThenByDescending(m => m.DetectedPartials.FirstOrDefault()?.Amplitude ?? -100)
                .ToList();

            _bestMeasurement = sorted.First();
        }

        private int GetQualityScore(NoteMeasurement m)
        {
            return m.Quality switch
            {
                "Groen" => 3,
                "Oranje" => 2,
                "Rood" => 1,
                _ => 0
            };
        }

        private void EmitRawSpectrumData(Complex[] fftBuffer)
        {
            if (RawSpectrumUpdated == null || !_hasTarget) return;

            int spectrumSize = ZeroPaddedFftSize / 2;
            double[] magnitudes = new double[spectrumSize];

            for (int i = 0; i < spectrumSize; i++)
            {
                magnitudes[i] = fftBuffer[i].Magnitude;
            }

            var spectrumData = new FftSpectrumData
            {
                Magnitudes = magnitudes,
                FrequencyResolution = (double)SampleRate / ZeroPaddedFftSize,
                TargetFrequency = _targetFreq,
                TargetMidiNote = _targetMidi,
                NoteName = PianoPhysics.MidiToNoteName(_targetMidi),
                Timestamp = DateTime.Now
            };

            RawSpectrumUpdated.Invoke(this, spectrumData);
        }

        /// <summary>
        /// Calculates RMS level in dB from audio samples.
        /// Used for attack detection to trigger measurement.
        /// </summary>
        private double CalculateRms(float[] samples)
        {
            if (samples.Length == 0) return -100.0;
            
            double sum = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                sum += samples[i] * samples[i];
            }
            
            double rms = Math.Sqrt(sum / samples.Length);
            return 20 * Math.Log10(rms + 1e-9); // Return in dB
        }
    }

    internal enum ScaleBreakRegion
    {
        None = 0,
        WoundStrings = 1,
        Transition = 2,
        PlainStrings = 3
    }
}
