using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AurisPianoTuner_measure.Models;
using MathNet.Numerics.IntegralTransforms;

namespace AurisPianoTuner_measure.Services
{
    public class FftAnalyzerService : IFftAnalyzerService
    {
        private const int FftSize = 32768; // 2^15 voor hoge resolutie
        private const int SampleRate = 96000;
        private readonly double[] _window;
        private readonly float[] _audioBuffer = new float[FftSize];
        private int _bufferWritePos = 0;

        private int _targetMidi;
        private double _targetFreq;
        private bool _hasTarget = false;
        private PianoMetadata? _pianoMetadata;

        public event EventHandler<NoteMeasurement>? MeasurementUpdated;
        
        /// <summary>
        /// Event voor raw FFT spectrum data (real-time visualisatie).
        /// Scientific basis: Smith (2011) - real-time spectral monitoring voor debug en analyse.
        /// Vuurt af bij elke FFT analyse, VOOR peak detection en averaging.
        /// </summary>
        public event EventHandler<FftSpectrumData>? RawSpectrumUpdated;

        // Logger property
        public ITestLoggerService? TestLogger { get; set; }

        // Low-frequency reference region for noise floor estimation (Strategy 3)
        // Scientific basis: Digital noise floor is frequency-independent (Smith 2011)
        // Region chosen to avoid typical piano fundamentals and low partials
        private const double NoiseRefStartHz = 100.0;  // Start of reference region
        private const double NoiseRefEndHz = 500.0;    // End of reference region

        /// <summary>
        /// Minimum reliable magnitude voor parabolic interpolation.
        /// 
        /// Scientific basis:
        /// - 24-bit ADC quantization noise: ~6e-8 magnitude
        /// - FFT processing with Blackman-Harris window: ~10x margin
        /// - Practical minimum for reliable log-domain interpolation: 1e-6
        /// 
        /// Values below this threshold indicate:
        /// - Edge of spectrum (near DC or Nyquist)
        /// - Silent frequency regions
        /// - Noise artifacts
        /// - Digital silence
        /// 
        /// In these cases, bin center frequency is used (less precise but still valid).
        /// Reference: Smith (2011) - "Spectral Audio Signal Processing"
        ///           Oppenheim & Schafer (2010) - "Discrete-Time Signal Processing"
        /// </summary>
        private const double MinReliableMagnitude = 1e-6;

        public FftAnalyzerService()
        {
            // Blackman-Harris window voor optimale spectrale resolutie
            _window = new double[FftSize];
            for (int i = 0; i < FftSize; i++)
            {
                _window[i] = 0.35875 
                           - 0.48829 * Math.Cos(2 * Math.PI * i / (FftSize - 1))
                           + 0.14128 * Math.Cos(4 * Math.PI * i / (FftSize - 1))
                           - 0.01168 * Math.Cos(6 * Math.PI * i / (FftSize - 1));
            }
        }

        public void SetPianoMetadata(PianoMetadata metadata)
        {
            _pianoMetadata = metadata;
            System.Diagnostics.Debug.WriteLine($"[FftAnalyzer] Piano metadata set: {metadata.Type}, {metadata.DimensionCm}cm, Scale Break: {GetNoteName(metadata.ScaleBreakMidiNote)}");
        }

        public void SetTargetNote(int midiIndex, double theoreticalFrequency)
        {
            _targetMidi = midiIndex;
            _targetFreq = theoreticalFrequency;
            _bufferWritePos = 0;
            _hasTarget = true;
        }

        public void Reset()
        {
            _bufferWritePos = 0;
            _hasTarget = false;
            Array.Clear(_audioBuffer, 0, _audioBuffer.Length);
        }

        public void ProcessAudioBuffer(float[] samples)
        {
            if (!_hasTarget) return;

            foreach (var sample in samples)
            {
                _audioBuffer[_bufferWritePos++] = sample;
                if (_bufferWritePos >= FftSize)
                {
                    Analyze();
                    // 50% overlap
                    Array.Copy(_audioBuffer, FftSize / 2, _audioBuffer, 0, FftSize / 2);
                    _bufferWritePos = FftSize / 2;
                }
            }
        }

        private void Analyze()
        {
            Complex[] fftBuffer = new Complex[FftSize];
            for (int i = 0; i < FftSize; i++)
                fftBuffer[i] = new Complex(_audioBuffer[i] * _window[i], 0);

            Fourier.Forward(fftBuffer, FourierOptions.NoScaling);

            // ============================================================
            // EMIT RAW SPECTRUM DATA (NIEUW in v1.8)
            // Voor real-time visualisatie VOOR processing
            // ============================================================
            EmitRawSpectrumData(fftBuffer);

            var result = new NoteMeasurement {
                MidiIndex = _targetMidi,
                TargetFrequency = _targetFreq,
                NoteName = GetNoteName(_targetMidi)
            };

            // Check if near scale break (�3 semitones for transitional zone)
            // Scientific basis: Askenfelt & Jansson (1990) - "Five Lectures on the Acoustics of the Piano"
            // Scale break marks physical transition from wound (copper-wrapped) to plain steel strings
            // This causes abrupt change in inharmonicity coefficient (factor 2-4 reduction)
            bool isNearScaleBreak = false;
            ScaleBreakRegion scaleBreakRegion = ScaleBreakRegion.None;
            
            if (_pianoMetadata != null)
            {
                int scaleBreak = _pianoMetadata.ScaleBreakMidiNote;
                int distance = _targetMidi - scaleBreak;
                
                // Define regions based on Fletcher & Rossing (1998) acoustic analysis
                if (Math.Abs(distance) <= 3)
                {
                    isNearScaleBreak = true;
                    
                    if (distance < -1)
                        scaleBreakRegion = ScaleBreakRegion.WoundStrings; // Below scale break: wound bass strings
                    else if (distance > 1)
                        scaleBreakRegion = ScaleBreakRegion.PlainStrings; // Above scale break: plain steel strings
                    else
                        scaleBreakRegion = ScaleBreakRegion.Transition;    // At scale break: critical transition zone
                }
            }

            // ============================================================
            // ITERATIVE CONVERGENCE METHOD: Correcte f0 en B berekening
            // Wetenschappelijke basis: Fletcher & Rossing (1998) - "The Physics of Musical Instruments"
            // Oplost circulaire afhankelijkheid tussen f? en B door iteratieve convergentie
            // ============================================================

            // PASS 1: Detecteer alle partials (gebruikt theoretische frequentie als zoekbasis)
            var detectedPartials = new List<PartialResult>();
            for (int n = 1; n <= 16; n++)
            {
                double searchCenterFreq = _targetFreq * n;
                var partial = FindPrecisePeak(fftBuffer, searchCenterFreq, n, _targetMidi, isNearScaleBreak);
                if (partial != null)
                {
                    detectedPartials.Add(partial);
                }
            }

            result.DetectedPartials = detectedPartials;

            // Stop als geen partials gevonden
            if (detectedPartials.Count == 0)
            {
                result.Quality = "Rood";
                result.MeasuredPartialNumber = 1;
                result.CalculatedFundamental = 0;
                result.InharmonicityCoefficient = 0;

                LogAndEmit(result, isNearScaleBreak, scaleBreakRegion);
                return;
            }

            // PASS 2: Initi�le f0 schatting (zonder B-correctie)
            // Gebruik partials met amplitude > -40 dB voor betrouwbare schatting
            var strongPartials = detectedPartials.Where(p => p.Amplitude > -40 && p.n >= 1 && p.n <= 8).ToList();

            double currentF0;
            if (strongPartials.Count >= 2)
            {
                // Gewogen gemiddelde: lagere partials krijgen hoger gewicht (betrouwbaarder)
                double weightedSum = 0;
                double totalWeight = 0;

                foreach (var p in strongPartials)
                {
                    double weight = 1.0 / p.n; // Inversie van partial nummer als gewicht
                    weightedSum += (p.Frequency / p.n) * weight;
                    totalWeight += weight;
                }

                currentF0 = weightedSum / totalWeight;
            }
            else if (strongPartials.Count == 1)
            {
                // Gebruik enige beschikbare partial
                currentF0 = strongPartials[0].Frequency / strongPartials[0].n;
            }
            else
            {
                // Fallback: gebruik theoretische frequentie
                currentF0 = _targetFreq;
            }

            // ITERATIVE CONVERGENCE: Verfijn f? en B door iteratie
            // Typisch convergeert dit in 2-3 iteraties met < 0.01 Hz verschil
            // Gebaseerd op Newton-Raphson convergentietheorie (Oppenheim & Schafer 2010)
            double currentB = 0.0001; // Initi�le schatting
            const int maxIterations = 5;
            const double convergenceThreshold = 0.01; // Hz - praktische precisie limiet voor piano tuning
            
            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                double previousF0 = currentF0;
                
                // Bereken B met huidige f?
                currentB = CalculateInharmonicityCoefficient(detectedPartials, currentF0, scaleBreakRegion);
                
                // Selecteer beste partial voor meting
                var measuredPartial = SelectBestPartialForMeasurement(detectedPartials, _targetMidi);
                
                if (measuredPartial != null)
                {
                    // Herbereken f? met nieuwe B (correcte inharmonicity compensatie)
                    // f_n = n�f0�sqrt(1 + B�n�)
                    // Oplossen naar f0: f0 = f_n / (n�sqrt(1 + B�n�))
                    double n = measuredPartial.n;
                    double fn = measuredPartial.Frequency;
                    double inharmonicityFactor = Math.Sqrt(1 + currentB * n * n);
                    
                    currentF0 = fn / (n * inharmonicityFactor);
                }
                
                // Check convergentie
                double delta = Math.Abs(currentF0 - previousF0);
                
                if (iteration > 0) // Skip logging voor eerste iteratie (baseline)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"  [Convergence] Iteration {iteration}: f0={currentF0:F2} Hz, " +
                        $"B={currentB:E4}, ?f0={delta:F4} Hz");
                }
                
                if (delta < convergenceThreshold)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"  [Convergence] Converged in {iteration + 1} iterations (?f0={delta:F4} Hz < {convergenceThreshold} Hz)");
                    break;
                }
            }

            // Store final converged values
            result.CalculatedFundamental = currentF0;
            result.InharmonicityCoefficient = currentB;
            
            // Store which partial was used for measurement
            var finalMeasuredPartial = SelectBestPartialForMeasurement(detectedPartials, _targetMidi);
            result.MeasuredPartialNumber = finalMeasuredPartial?.n ?? 1;

            // Bepaal kwaliteit met scale break awareness
            // Bij scale break: verhoogde quality threshold vanwege spectrale complexiteit
            if (isNearScaleBreak && scaleBreakRegion == ScaleBreakRegion.Transition)
            {
                // Transition zone: require more partials for "Groen" rating
                result.Quality = detectedPartials.Count > 7 ? "Groen" :
                               detectedPartials.Count > 4 ? "Oranje" : "Rood";
            }
            else
            {
                // Standard quality assessment
                result.Quality = detectedPartials.Count > 5 ? "Groen" :
                               detectedPartials.Count > 2 ? "Oranje" : "Rood";
            }

            LogAndEmit(result, isNearScaleBreak, scaleBreakRegion);
        }

        /// <summary>
        /// Berekent de inharmoniciteitscoefficient (B) uit gedetecteerde partials.
        /// 
        /// SCALE BREAK FIX (v1.6):
        /// Bij transition zone (wound ? plain strings) worden partials NIET meer gemixed.
        /// In plaats daarvan: conservatieve B-estimate OF alleen laagste partials (n=2-5).
        /// 
        /// Wetenschappelijke basis:
        /// - Askenfelt & Jansson (1990): "At scale break, B changes abruptly by factor 2-4"
        /// - Fletcher & Rossing (1998): Wound strings have 3-5x higher B than plain strings
        /// - Conklin (1996): "Design and Tone in the Mechanoacoustic Piano"
        /// </summary>
        private double CalculateInharmonicityCoefficient(List<PartialResult> partials, double estimatedF0, ScaleBreakRegion region)
        {
            // ============================================================
            // SPECIAL HANDLING: TRANSITION ZONE
            // ============================================================
            if (region == ScaleBreakRegion.Transition)
            {
                System.Diagnostics.Debug.WriteLine($"  [B-Calc] TRANSITION ZONE detected - using special handling");
                
                // Strategy 1: Try to use only LOW partials (n=2,3,4,5) which are more reliable
                // Higher partials may be from mixed string types causing incorrect B
                var lowPartials = partials
                    .Where(p => p.Amplitude > -50 && p.n >= 2 && p.n <= 5)
                    .ToList();
                
                if (lowPartials.Count >= 3)
                {
                    System.Diagnostics.Debug.WriteLine($"  [B-Calc] Using {lowPartials.Count} low partials (n=2-5) for transition zone");
                    
                    // Perform least-squares regression with ONLY low partials
                    double transitionSumX = 0, transitionSumY = 0, transitionSumXY = 0, transitionSumXX = 0;
                    int transitionCount = 0;

                    foreach (var p in lowPartials)
                    {
                        double n = p.n;
                        double fn = p.Frequency;
                        double x = n * n;
                        double ratio = fn / (n * estimatedF0);
                        double y = ratio * ratio - 1.0;

                        // Relaxed outlier threshold for transition zone (80% instead of 70%)
                        if (y < -0.1 || y > 0.8)
                        {
                            System.Diagnostics.Debug.WriteLine($"  [B-Calc] Transition outlier rejected: n={p.n}, fn={fn:F2}, y={y:F4}");
                            continue;
                        }

                        transitionSumX += x;
                        transitionSumY += y;
                        transitionSumXY += x * y;
                        transitionSumXX += x * x;
                        transitionCount++;
                    }

                    if (transitionCount >= 2)
                    {
                        double transitionDenominator = transitionCount * transitionSumXX - transitionSumX * transitionSumX;
                        if (Math.Abs(transitionDenominator) > 1e-10)
                        {
                            double transitionB = (transitionCount * transitionSumXY - transitionSumX * transitionSumY) / transitionDenominator;
                            transitionB = Math.Max(0.00001, Math.Min(0.01, transitionB));
                            
                            System.Diagnostics.Debug.WriteLine(
                                $"  [B-Calc] TRANSITION: f0={estimatedF0:F2} Hz, B={transitionB:E3} (from {transitionCount} low partials)");
                            System.Diagnostics.Debug.WriteLine(
                                $"  [B-Calc WARNING] Transition zone B may be unreliable due to mixed string types");
                            
                            return transitionB;
                        }
                    }
                }
                
                // Strategy 2: Insufficient low partials ? Use conservative estimate based on MIDI position
                // This prevents mixing wound+plain partial data
                System.Diagnostics.Debug.WriteLine($"  [B-Calc] Insufficient low partials - using conservative estimate");
                
                if (_pianoMetadata != null)
                {
                    int scaleBreak = _pianoMetadata.ScaleBreakMidiNote;
                    
                    if (_targetMidi < scaleBreak)
                    {
                        // Wound string estimate (conservative high value)
                        double conservativeB = 0.0006;
                        System.Diagnostics.Debug.WriteLine(
                            $"  [B-Calc] TRANSITION (wound side): Using conservative B={conservativeB:E3} " +
                            $"(MIDI {_targetMidi} < scale break {scaleBreak})");
                        return conservativeB;
                    }
                    else
                    {
                        // Plain string estimate (conservative low value)
                        double conservativeB = 0.0002;
                        System.Diagnostics.Debug.WriteLine(
                            $"  [B-Calc] TRANSITION (plain side): Using conservative B={conservativeB:E3} " +
                            $"(MIDI {_targetMidi} >= scale break {scaleBreak})");
                        return conservativeB;
                    }
                }
                
                // Fallback: no metadata available
                System.Diagnostics.Debug.WriteLine($"  [B-Calc WARNING] No piano metadata for transition zone - using default");
                return 0.0003;
            }

            // ============================================================
            // STANDARD HANDLING: NON-TRANSITION ZONES
            // ============================================================
            
            var validPartials = partials.Where(p => p.Amplitude > -50 && p.n >= 2 && p.n <= 12).ToList();

            if (validPartials.Count < 3)
            {
                // Fallback with region-aware default
                double fallbackB = region switch
                {
                    ScaleBreakRegion.WoundStrings => 0.0005,  // Wound strings
                    ScaleBreakRegion.PlainStrings => 0.0002,  // Plain strings
                    _ => 0.0001  // Unknown/default
                };
                
                System.Diagnostics.Debug.WriteLine(
                    $"  [B-Calc] Insufficient partials ({validPartials.Count}), using fallback B={fallbackB:E3} for {region}");
                
                return fallbackB;
            }

            // Standard least-squares regression
            double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;
            int count = 0;

            foreach (var p in validPartials)
            {
                double n = p.n;
                double fn = p.Frequency;
                double x = n * n;
                double ratio = fn / (n * estimatedF0);
                double y = ratio * ratio - 1.0;

                // Standard outlier detection (50% threshold)
                if (y < -0.1 || y > 0.5)
                {
                    System.Diagnostics.Debug.WriteLine($"  [B-Calc] Outlier rejected: n={p.n}, fn={fn:F2}, ratio={ratio:F4}, y={y:F4}");
                    continue;
                }

                sumX += x;
                sumY += y;
                sumXY += x * y;
                sumXX += x * x;
                count++;
            }

            if (count < 2)
            {
                double fallbackB = region switch
                {
                    ScaleBreakRegion.WoundStrings => 0.0005,
                    ScaleBreakRegion.PlainStrings => 0.0002,
                    _ => 0.0001
                };
                return fallbackB;
            }

            // Least-squares fit: B = (N�?XY - ?X�?Y) / (N�?XX - (?X)�)
            double denominator = count * sumXX - sumX * sumX;

            if (Math.Abs(denominator) < 1e-10)
            {
                return 0.0001;
            }

            double B = (count * sumXY - sumX * sumY) / denominator;

            // Saniteer: B moet in fysisch realistische range liggen
            B = Math.Max(0.00001, Math.Min(0.01, B));

            // Enhanced validation with region-specific checks
            if (region != ScaleBreakRegion.None)
            {
                string regionWarning = ValidateBCoefficientForRegion(B, region);
                if (!string.IsNullOrEmpty(regionWarning))
                {
                    System.Diagnostics.Debug.WriteLine($"  [B-Calc WARNING] {regionWarning}");
                }
            }

            System.Diagnostics.Debug.WriteLine($"  [B-Calc] f0={estimatedF0:F2} Hz, B={B:E3}, region={region}, using {count} partials");

            return B;
        }

        /// <summary>
        /// Valideert of B-coefficient consistent is met verwachte waarde voor scale break regio.
        /// Gebaseerd op Conklin (1996) - "Design and Tone in the Mechanoacoustic Piano"
        /// 
        /// Enhanced in v1.6 met strengere validation voor wound/plain strings.
        /// </summary>
        private string ValidateBCoefficientForRegion(double B, ScaleBreakRegion region)
        {
            // Expected B ranges based on scientific literature (Fletcher & Rossing 1998, Conklin 1996)
            return region switch
            {
                // Wound strings: typical range 300-1000�10??
                ScaleBreakRegion.WoundStrings when B < 0.0003 =>
                    $"Low B ({B:E3}) for wound strings (expected 300-1000�10??). Possible plain string or measurement error.",
                
                ScaleBreakRegion.WoundStrings when B > 0.001 =>
                    $"High B ({B:E3}) for wound strings (expected < 1000�10??). Check string condition or measurement.",
                
                // Plain strings: typical range 50-400�10??
                ScaleBreakRegion.PlainStrings when B < 0.00005 =>
                    $"Very low B ({B:E3}) for plain strings (expected 50-400�10??). Unusual for acoustic piano.",
                
                ScaleBreakRegion.PlainStrings when B > 0.0005 =>
                    $"High B ({B:E3}) for plain strings (expected < 500�10??). Possible wound string or measurement error.",
                
                // Transition zone - always warn
                ScaleBreakRegion.Transition =>
                    $"Transition zone: B={B:E3}. Value calculated from limited low partials (n=2-5) of conservative estimate.",
                
                _ => string.Empty
            };
        }

        private int GetOptimalPartialForRegister(int midiIndex)
        {
            return midiIndex switch
            {
                <= 35 => 6,    // Deep Bass (A0-B1)
                <= 47 => 3,    // Bass (C2-B2)
                <= 60 => 2,    // Tenor (C3-C4)
                _ => 1         // Mid-High & Treble (C#4+)
            };
        }

        private PartialResult? SelectBestPartialForMeasurement(List<PartialResult> partials, int midiIndex)
        {
            if (partials == null || partials.Count == 0) return null;

            int optimalN = GetOptimalPartialForRegister(midiIndex);

            // Zoek optimale partial
            var optimalPartial = partials.FirstOrDefault(p => p.n == optimalN);

            if (optimalPartial != null && optimalPartial.Amplitude > -60)
            {
                return optimalPartial;
            }

            // Fallback: zoek sterkste partial in acceptabel bereik
            var acceptablePartials = midiIndex switch
            {
                <= 35 => partials.Where(p => p.n >= 4 && p.n <= 8),
                <= 47 => partials.Where(p => p.n >= 2 && p.n <= 4),
                <= 60 => partials.Where(p => p.n >= 1 && p.n <= 3),
                _ => partials.Where(p => p.n == 1)
            };

            return acceptablePartials.OrderByDescending(p => p.Amplitude).FirstOrDefault();
        }

        private PartialResult? FindPrecisePeak(Complex[] fftData, double targetFreq, int n, int midiIndex, bool isNearScaleBreak)
        {
            // Scientific basis: Smith (2011) - "Spectral Audio Signal Processing"
            // Minimum 3 bins required for reliable parabolic interpolation
            const int MinSearchBins = 3;

            double binFreq = (double)SampleRate / FftSize;
            int centerBin = (int)(targetFreq / binFreq);

            // Scientific approach: Search window based on register, partial number, and expected inharmonicity
            // Based on Fletcher & Rossing (1998) - inharmonicity causes higher partials to shift upward
            // and Smith (2011) - spectral peak detection requires adequate frequency resolution
            
            // Base search window in cents (varies by register)
            // Deep bass: wider due to low SNR and string noise
            // Treble: narrower due to cleaner spectrum
            double baseSearchCents = midiIndex switch
            {
                <= 35 => 30.0,  // Deep bass: A0-B1 (Askenfelt & Jansson 1990)
                <= 47 => 25.0,  // Bass: C2-B2
                <= 60 => 20.0,  // Tenor: C3-C4
                <= 72 => 15.0,  // Mid-high: C#4-C5
                _ => 12.0       // Treble: C#5+ (cleaner spectrum)
            };

            // SCALE BREAK COMPENSATION (Askenfelt & Jansson 1990, Fletcher & Rossing 1998)
            // At scale break transition (wound ? plain strings), inharmonicity changes abruptly (factor 2-4)
            // This causes frequency shifts that exceed normal search windows, especially for higher partials
            // Expand search window by +40% within �3 semitones of scale break to prevent missed detections
            if (isNearScaleBreak)
            {
                baseSearchCents *= 1.4; // 40% expansion for scale break compensation
                System.Diagnostics.Debug.WriteLine($"  [Scale Break] Expanded search window: {baseSearchCents:F1} cents for n={n}");
            }

            // Expand search window for higher partials due to cumulative inharmonicity uncertainty
            // For n=8 with B=0.0004, frequency shift can exceed 25 cents (Fletcher & Rossing 1998)
            double partialExpansionFactor = 1.0 + (n - 1) * 0.15; // 15% expansion per partial beyond n=1
            double effectiveSearchCents = baseSearchCents * partialExpansionFactor;

            // Cap maximum search window to prevent false positives
            effectiveSearchCents = Math.Min(effectiveSearchCents, 100.0); // Max ~100 cents (increased from 80 for scale break handling)

            // ============================================================
            // IMPROVED SEARCH WINDOW CALCULATION
            // Scientific basis: Fletcher & Rossing (1998), Smith (2011)
            // ============================================================

            // 1. Convert cents to Hz
            double searchWindowHz = targetFreq * (Math.Pow(2, effectiveSearchCents / 1200.0) - 1);

            // 2. Frequency-dependent minimum search window
            // Scientific basis: Deep bass has higher relative frequency uncertainty
            // - Fletcher & Rossing (1998): "The Physics of Musical Instruments"
            //   Deep bass partials have shorter decay times and lower SNR
            // - Smith (2011): Minimum frequency resolution needed for reliable peak detection
            double minSearchHz = targetFreq switch
            {
                < 50   => 2.0,  // A0-G0: minimum 2 Hz absolute (?13-27 cents)
                < 100  => 3.0,  // G#0-B1: minimum 3 Hz absolute (?17-35 cents)
                < 200  => 4.0,  // C2-G2: minimum 4 Hz absolute (?12-24 cents)
                _      => 0.0   // Higher frequencies: no additional minimum needed
            };

            // 3. Apply frequency-dependent minimum
            double effectiveSearchHz = Math.Max(searchWindowHz, minSearchHz);

            // 4. Convert to bins with absolute minimum for parabolic interpolation
            int searchRange = Math.Max(MinSearchBins, (int)(effectiveSearchHz / binFreq));

            // 5. Log warning if significant search window expansion occurred
            if (effectiveSearchHz > searchWindowHz * 1.5)
            {
                double actualSearchCents = 1200 * Math.Log2((effectiveSearchHz / targetFreq) + 1);
                System.Diagnostics.Debug.WriteLine(
                    $"  [Deep Bass] Search window expanded: {effectiveSearchCents:F1} ? {actualSearchCents:F1} cents " +
                    $"({searchWindowHz:F2} ? {effectiveSearchHz:F2} Hz) for {targetFreq:F1} Hz, n={n}");
            }

            // Safety bounds: prevent array access violations
            int minBin = Math.Max(1, centerBin - searchRange);
            int maxBin = Math.Min(FftSize / 2 - 2, centerBin + searchRange);

            int bestBin = -1;
            double maxMag = -1;

            for (int i = minBin; i <= maxBin; i++)
            {
                double mag = fftData[i].Magnitude;
                if (mag > maxMag) { maxMag = mag; bestBin = i; }
            }

            // ============================================================
            // ADAPTIVE NOISE THRESHOLD (Oppenheim & Schafer 2010, Smith 2011)
            // ============================================================
            
            // Base threshold values (empirically determined with ECM8000 mic + UMC202HD @ 96kHz)
            // Validated on Fazer Console 107cm (2026-01-09)
            double baseNoiseThreshold = (targetFreq, n) switch
            {
                ( < 60, _) => 0.003,      // Deep bass (below ~B0): very weak fundamental
                ( < 100, >= 6) => 0.002,  // Low bass, high partials
                ( < 100, _) => 0.0015,    // Low bass, low partials
                ( < 500, >= 4) => 0.0012, // Mid-range, high partials
                ( < 1000, _) => 0.001,    // Mid-range
                (_, >= 8) => 0.0008,      // High partials (weaker)
                _ => 0.0006               // Treble range (strong)
            };

            // Estimate local noise floor for adaptive thresholding
            // Scientific basis: Oppenheim & Schafer (2010) - "Spectral Analysis of Random Signals"
            double estimatedNoiseFloor = EstimateNoiseFloor(fftData, centerBin, searchRange);
            
            // SNR requirement: minimum 3:1 signal-to-noise ratio (~9.5 dB)
            // Based on Kay & Marple (1981) - "Spectrum Analysis - A Modern Perspective"
            double snrRequirement = 3.0;
            double snrBasedThreshold = estimatedNoiseFloor * snrRequirement;
            
            // Use maximum of base threshold and SNR-based threshold for robustness
            // This ensures:
            // - Quiet environments: SNR-based threshold prevents false positives
            // - Noisy environments: Maintains minimum detection capability
            double adaptiveThreshold = Math.Max(snrBasedThreshold, baseNoiseThreshold);

            // Scale break compensation: increase threshold by 20% to compensate for wider search window
            // Wider window increases probability of spurious peak detection
            if (isNearScaleBreak)
            {
                adaptiveThreshold *= 1.2;
            }

            // Threshold validation check
            if (maxMag < adaptiveThreshold || bestBin < 1 || bestBin >= FftSize / 2 - 1)
            {
                // Debug logging for rejected peaks (Smith 2011 - diagnostic best practice)
                if (maxMag > 0)
                {
                    double actualSnr = maxMag / Math.Max(estimatedNoiseFloor, 1e-10);
                    System.Diagnostics.Debug.WriteLine(
                        $"  [Peak Reject - Threshold] n={n}, freq={targetFreq:F1} Hz, " +
                        $"mag={maxMag:E4} < threshold={adaptiveThreshold:E4}, " +
                        $"SNR={actualSnr:F2}:1 (required {snrRequirement}:1), " +
                        $"noise_floor={estimatedNoiseFloor:E4}");
                }
                return null;
            }

            // ============================================================
            // PARABOLIC INTERPOLATION - NUMERICALLY STABLE VERSION
            // Scientific basis: Smith (2011) - "Spectral Audio Signal Processing"
            //                   Oppenheim & Schafer (2010) - "Discrete-Time Signal Processing"
            // ============================================================

            // Get neighbor magnitudes
            double magPrev = fftData[bestBin - 1].Magnitude;
            double magPeak = fftData[bestBin].Magnitude;
            double magNext = fftData[bestBin + 1].Magnitude;

            // Validate neighbors are above reliable threshold
            // If not: peak is at edge, in silent region, or noise artifact
            if (magPrev < MinReliableMagnitude || magNext < MinReliableMagnitude)
            {
                // Cannot perform reliable parabolic interpolation
                // Use bin center frequency (still valid, just less precise)
                double centerFreq = bestBin * binFreq;
                
                System.Diagnostics.Debug.WriteLine(
                    $"  [Parabolic] Weak neighbors for n={n} at {targetFreq:F1} Hz: " +
                    $"prev={magPrev:E3}, peak={magPeak:E3}, next={magNext:E3}. " +
                    $"Using bin center: {centerFreq:F2} Hz");
                
                return new PartialResult {
                    n = n,
                    Frequency = centerFreq,
                    Amplitude = 20 * Math.Log10(maxMag)
                };
            }

            // Additional validation: Peak should be significantly stronger than neighbors
            // This ensures we have a real peak, not a noise plateau
            // Scientific basis: For Blackman-Harris window, main lobe width is ~8 bins at -3dB
            // Neighbors at ±1 bin are typically 0.84× peak magnitude for sinusoidal signals (-1.5 dB)
            // Threshold reduced from 1.5 to 1.15 to accommodate window characteristics while rejecting noise
            double maxNeighbor = Math.Max(magPrev, magNext);
            if (magPeak < 1.15 * maxNeighbor)
            {
                // Not a clear peak - likely noise or sidelobe
                System.Diagnostics.Debug.WriteLine(
                    $"  [Parabolic Reject] Insufficient peak prominence for n={n} at {targetFreq:F1} Hz: " +
                    $"peak={magPeak:E3}, max_neighbor={maxNeighbor:E3}, ratio={magPeak/maxNeighbor:F2}");
                return null;
            }

            // Apply conservative floor for logarithmic interpolation
            // Use 1e-6 instead of 1e-10 (based on physical system limits)
            double magPrevSafe = Math.Max(magPrev, MinReliableMagnitude);
            double magPeakSafe = Math.Max(magPeak, MinReliableMagnitude);
            double magNextSafe = Math.Max(magNext, MinReliableMagnitude);

            // Logarithmic parabolic interpolation (Smith 2011)
            double y1 = Math.Log(magPrevSafe);
            double y2 = Math.Log(magPeakSafe);
            double y3 = Math.Log(magNextSafe);

            double denominator = y1 - 2 * y2 + y3;

            // Check for degenerate parabola (flat top, numerical issues)
            if (Math.Abs(denominator) < 1e-10)
            {
                // Parabola is too flat, use bin center
                double centerFreq = bestBin * binFreq;
                
                System.Diagnostics.Debug.WriteLine(
                    $"  [Parabolic] Degenerate parabola for n={n} at {targetFreq:F1} Hz: " +
                    $"denominator={denominator:E3}. Using bin center: {centerFreq:F2} Hz");
                
                return new PartialResult {
                    n = n,
                    Frequency = centerFreq,
                    Amplitude = 20 * Math.Log10(maxMag)
                };
            }

            // Calculate interpolation offset
            double d = (y1 - y3) / (2 * denominator);

            // Sanity check: offset should be reasonable (|d| < 1)
            // Values > 1 indicate interpolation error or broad peak
            if (Math.Abs(d) > 1.0)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"  [Parabolic] Excessive offset for n={n} at {targetFreq:F1} Hz: " +
                    $"d={d:F3}. Using bin center.");
                
                return new PartialResult {
                    n = n,
                    Frequency = bestBin * binFreq,
                    Amplitude = 20 * Math.Log10(maxMag)
                };
            }

            // Calculate precise frequency with sub-bin interpolation
            double preciseFreq = (bestBin + d) * binFreq;

            // Strict validation: reject if deviation exceeds search window
            // This prevents accepting spurious peaks or harmonics of adjacent strings
            double actualDeviation = Math.Abs(preciseFreq - targetFreq);
            if (actualDeviation > searchWindowHz)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"  [Peak Reject - Deviation] n={n}, target={targetFreq:F2} Hz, found={preciseFreq:F2} Hz, " +
                    $"deviation={actualDeviation:F2} Hz (limit={searchWindowHz:F2} Hz)");
                return null;
            }

            // Additional validation for very large deviations
            // Scale break: allow larger deviations (up to 120 cents for n>4)
            double maxAllowedCents = n <= 4 ? 50.0 : (isNearScaleBreak ? 120.0 : 100.0);
            double actualCents = 1200 * Math.Log2(preciseFreq / targetFreq);
            if (Math.Abs(actualCents) > maxAllowedCents)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"  [Peak Reject - Cents] n={n}, deviation={actualCents:F1} cents exceeds limit {maxAllowedCents:F1} cents");
                return null;
            }

            return new PartialResult {
                n = n,
                Frequency = preciseFreq,
                Amplitude = 20 * Math.Log10(maxMag)
            };
        }

        /// <summary>
        /// Schat lokale noise floor in FFT spectrum voor adaptive peak detection.
        /// 
        /// Scientific basis:
        /// - Oppenheim & Schafer (2010) - "Discrete-Time Signal Processing", Chapter 10.2
        /// - Kay & Marple (1981) - "Spectrum Analysis - A Modern Perspective", IEEE Proceedings
        /// - Smith (2011) - "Spectral Audio Signal Processing"
        /// 
        /// Multi-strategy approach:
        /// 1. Sample around signal (primary)
        /// 2. Sample below signal (fallback)
        /// 3. Sample from low-frequency reference region (treble fallback)
        /// 4. Hardcoded conservative estimate (last resort)
        /// 
        /// Wetenschappelijke basis voor strategy 3:
        /// Noise floor in digital audio systemen is frequency-independent (dominated door 
        /// ADC quantization noise, thermal noise, en electronic noise floor).
        /// Sampling from low frequencies (100-500 Hz) provides valid reference for hele spectrum.
        /// </summary>
        /// <param name="fftData">FFT magnitude spectrum</param>
        /// <param name="centerBin">Centrale bin waar signaal verwacht wordt</param>
        /// <param name="searchRange">Zoekbereik rondom signaal (in bins)</param>
        /// <returns>Geschatte noise floor magnitude (lineair, niet dB)</returns>
        private double EstimateNoiseFloor(Complex[] fftData, int centerBin, int searchRange)
        {
            var noiseSamples = new List<double>();
            
            // ============================================================
            // STRATEGY 1: Sample bins around signal (primary method)
            // ============================================================
            
            int exclusionZone = (int)(2.5 * searchRange);
            int sampleStart = Math.Max(1, centerBin - 4 * searchRange);
            int sampleEnd = Math.Min(fftData.Length / 2 - 1, centerBin + 4 * searchRange);
            
            // Sample bins met voldoende afstand tot signaal
            for (int i = sampleStart; i < sampleEnd; i += Math.Max(1, searchRange / 2))
            {
                if (Math.Abs(i - centerBin) > exclusionZone)
                {
                    noiseSamples.Add(fftData[i].Magnitude);
                }
            }
            
            if (noiseSamples.Count >= 5)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"  [Noise Floor] Strategy 1 (around signal): {noiseSamples.Count} samples, " +
                    $"bin {centerBin}, freq {centerBin * ((double)SampleRate / FftSize):F1} Hz");
                return CalculateMedianNoiseFloor(noiseSamples);
            }
            
            // ============================================================
            // STRATEGY 2: Sample below signal (improved fallback)
            // ============================================================
            
            noiseSamples.Clear();
            
            // Sample from much wider range below signal
            int belowSampleStart = 1;
            int belowSampleEnd = Math.Max(1, centerBin - exclusionZone);
            int belowStepSize = Math.Max(1, exclusionZone / 8); // Smaller step for more samples
            
            for (int i = belowSampleStart; i < belowSampleEnd && noiseSamples.Count < 30; i += belowStepSize)
            {
                noiseSamples.Add(fftData[i].Magnitude);
            }
            
            if (noiseSamples.Count >= 5)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"  [Noise Floor] Strategy 2 (below signal): {noiseSamples.Count} samples");
                return CalculateMedianNoiseFloor(noiseSamples);
            }
            
            // ============================================================
            // STRATEGY 3: Sample from low-frequency reference region (NEW!)
            // Scientific basis: Noise floor is frequency-independent in digital systems
            // ============================================================
            
            noiseSamples.Clear();
            
            // Define reference region: 100-500 Hz (always has content, far from most partials)
            // Scientific basis: Smith (2011) - digital noise floor is constant across spectrum
            int refStartBin = (int)(NoiseRefStartHz / ((double)SampleRate / FftSize));  // ~34 bins @ 96kHz
            int refEndBin = (int)(NoiseRefEndHz / ((double)SampleRate / FftSize));    // ~171 bins @ 96kHz
            
            // Only use this if signal is NOT in this region
            if (centerBin > refEndBin + exclusionZone)
            {
                for (int i = refStartBin; i < refEndBin && noiseSamples.Count < 30; i += 3)
                {
                    noiseSamples.Add(fftData[i].Magnitude);
                }
                
                if (noiseSamples.Count >= 5)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"  [Noise Floor] Strategy 3 (low-freq reference): {noiseSamples.Count} samples " +
                        $"for high freq bin {centerBin} ({centerBin * ((double)SampleRate / FftSize):F1} Hz)");
                    return CalculateMedianNoiseFloor(noiseSamples);
                }
            }
            
            // ============================================================
            // STRATEGY 4: Hardcoded conservative estimate (last resort)
            // ============================================================
            
            System.Diagnostics.Debug.WriteLine(
                $"  [Noise Floor WARNING] All strategies failed, using hardcoded 0.0001 " +
                $"for bin {centerBin} ({centerBin * ((double)SampleRate / FftSize):F1} Hz)");
            
            // Conservative estimate for modern 24-bit interfaces
            // Presonus Studio 24c: -101 dBFS ? 0.00003 magnitude
            // Use slightly higher value (0.0001) as safety margin
            return 0.0001;
        }

        /// <summary>
        /// Berekent median noise floor uit samples met sanity checks.
        /// </summary>
        private double CalculateMedianNoiseFloor(List<double> noiseSamples)
        {
            if (noiseSamples.Count == 0)
            {
                return 0.0001;
            }
            
            // Sort voor median
            noiseSamples.Sort();
            
            // Median (robust tegen outliers - Kay & Marple 1981)
            double medianNoiseFloor = noiseSamples[noiseSamples.Count / 2];
            
            // Sanity check: noise floor moet fysisch realistisch zijn
            // Te laag: mogelijk digital silence (gebruik minimum)
            // Te hoog: mogelijk signaal lekken (cap maximum)
            medianNoiseFloor = Math.Max(1e-6, Math.Min(0.01, medianNoiseFloor));
            
            return medianNoiseFloor;
        }

        private void LogAndEmit(NoteMeasurement result, bool isNearScaleBreak, ScaleBreakRegion region)
        {
            // Logging
            if (_pianoMetadata != null)
            {
                string scaleBreakInfo = isNearScaleBreak 
                    ? $" [SCALE BREAK: {region}]" 
                    : "";
                
                System.Diagnostics.Debug.WriteLine(
                    $"[{_pianoMetadata.Type}] {result.NoteName} (MIDI {_targetMidi}): " +
                    $"{result.DetectedPartials.Count} partials, " +
                    $"f0={result.CalculatedFundamental:F2} Hz, " +
                    $"B={result.InharmonicityCoefficient:E4}{scaleBreakInfo}");
            }

            // Event uitzending
            MeasurementUpdated?.Invoke(this, result);
        }

        private static readonly string[] NoteNames = {
            "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"
        };

        private string GetNoteName(int midiNote)
        {
            int noteIndex = midiNote % 12;
            int octave = midiNote / 12 - 1;

            return $"{NoteNames[noteIndex]}{octave}";
        }

        /// <summary>
        /// Emits raw FFT spectrum data voor real-time visualisatie.
        /// Scientific basis: Smith (2011) - "Spectral Audio Signal Processing"
        /// 
        /// Verzend het volledige magnitude spectrum (0 Hz tot Nyquist) naar subscribers.
        /// Dit gebeurt VOOR peak detection en averaging, dus toont raw FFT resultaten.
        /// </summary>
        /// <param name="fftBuffer">Complex FFT resultaat (positive + negative frequencies)</param>
        private void EmitRawSpectrumData(Complex[] fftBuffer)
        {
            // Check of er subscribers zijn (performance optimization)
            if (RawSpectrumUpdated == null || !_hasTarget)
                return;

            // Alleen positive frequencies (0 Hz tot Nyquist)
            int spectrumSize = FftSize / 2;
            double[] magnitudes = new double[spectrumSize];

            for (int i = 0; i < spectrumSize; i++)
            {
                magnitudes[i] = fftBuffer[i].Magnitude;
            }

            var spectrumData = new FftSpectrumData
            {
                Magnitudes = magnitudes,
                FrequencyResolution = (double)SampleRate / FftSize,
                TargetFrequency = _targetFreq,
                TargetMidiNote = _targetMidi,
                NoteName = GetNoteName(_targetMidi),
                Timestamp = DateTime.Now
            };

            // Emit event (invoked op FFT analyzer thread)
            RawSpectrumUpdated.Invoke(this, spectrumData);
        }
    }

    /// <summary>
    /// Classificatie van positie relatief tot scale break.
    /// Gebaseerd op Askenfelt & Jansson (1990) - "Five Lectures on the Acoustics of the Piano"
    /// </summary>
    internal enum ScaleBreakRegion
    {
        /// <summary>Niet in de buurt van scale break (> 3 semitones afstand)</summary>
        None = 0,
        
        /// <summary>Wound (copper-wrapped) bass strings - hogere inharmonicity</summary>
        WoundStrings = 1,
        
        /// <summary>Critical transition zone (�1 semitone) - abrupte verandering in B-coefficient</summary>
        Transition = 2,
        
        /// <summary>Plain steel strings - lagere inharmonicity, helderder spectrum</summary>
        PlainStrings = 3
    }
}
