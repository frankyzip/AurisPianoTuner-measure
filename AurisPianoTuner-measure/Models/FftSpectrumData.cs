using System;

namespace AurisPianoTuner_measure.Models
{
    /// <summary>
    /// Raw FFT spectrum data voor real-time visualisatie.
    /// Scientific basis: Smith (2011) - "Spectral Audio Signal Processing"
    /// 
    /// Gebruikt voor:
    /// - Real-time spectrum display in SpectrumVisualizer
    /// - Debug visualisatie van FFT window en peaks
    /// - Inzicht in signaal kwaliteit en ruis
    /// </summary>
    public class FftSpectrumData
    {
        /// <summary>
        /// FFT magnitude spectrum (linear scale, niet dB).
        /// Array lengte = FftSize/2 (0 Hz tot Nyquist)
        /// </summary>
        public double[] Magnitudes { get; set; } = Array.Empty<double>();

        /// <summary>
        /// Frequentie resolutie per bin (Hz).
        /// Berekend als: SampleRate / FftSize
        /// Voorbeeld: 96000 Hz / 32768 = 2.93 Hz/bin
        /// </summary>
        public double FrequencyResolution { get; set; }

        /// <summary>
        /// Theoretische target frequentie van de gemeten noot (Hz).
        /// Gebruikt als referentie voor visualisatie centrering.
        /// </summary>
        public double TargetFrequency { get; set; }

        /// <summary>
        /// MIDI note number (21-108 voor piano: A0-C8)
        /// </summary>
        public int TargetMidiNote { get; set; }

        /// <summary>
        /// Menselijk leesbare nootnaam (bijv. "A4", "C#3")
        /// </summary>
        public string NoteName { get; set; } = string.Empty;

        /// <summary>
        /// Timestamp van de FFT analyse (voor drift visualisatie)
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
