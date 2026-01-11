using System;

namespace AurisPianoTuner_measure.Utils
{
    /// <summary>
    /// Helper methodes voor piano fysica berekeningen.
    /// Scientific basis: Fletcher & Rossing (1998)
    /// </summary>
    public static class PianoPhysics
    {
        /// <summary>
        /// Berekent theoretische frequentie voor een MIDI noot (equal temperament, A4=440Hz).
        /// Formula: f = 440 * 2^((n-69)/12)
        /// </summary>
        public static double MidiToFrequency(int midiNote)
        {
            return 440.0 * Math.Pow(2.0, (midiNote - 69) / 12.0);
        }

        /// <summary>
        /// Berekent cent deviation tussen twee frequenties.
        /// Formula: cents = 1200 * log2(f_measured / f_theoretical)
        /// </summary>
        public static double FrequencyToCents(double measuredFreq, double theoreticalFreq)
        {
            if (theoreticalFreq <= 0 || measuredFreq <= 0) return 0;
            return 1200.0 * Math.Log2(measuredFreq / theoreticalFreq);
        }

        /// <summary>
        /// Converteert MIDI nummer naar nootnaam (bijv. 69 ? "A4").
        /// </summary>
        public static string MidiToNoteName(int midiNote)
        {
            string[] noteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
            int noteInOctave = midiNote % 12;
            int octave = (midiNote / 12) - 1;
            return noteNames[noteInOctave] + octave.ToString();
        }

        /// <summary>
        /// Berekent verwachte frequentie van partial n met inharmonicity.
        /// Fletcher & Rossing formula: f_n = n * f0 * sqrt(1 + B * n^2)
        /// </summary>
        public static double CalculatePartialFrequency(int n, double f0, double B)
        {
            return n * f0 * Math.Sqrt(1 + B * n * n);
        }

        /// <summary>
        /// Berekent fundamental uit partial frequency met inharmonicity correctie.
        /// Inverse van CalculatePartialFrequency.
        /// </summary>
        public static double CalculateFundamentalFromPartial(int n, double fn, double B)
        {
            double inharmonicityFactor = Math.Sqrt(1 + B * n * n);
            return fn / (n * inharmonicityFactor);
        }
    }
}