using System;
using System.Collections.Generic;

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

        // ============================================================
        // NIEUW: Inharmonicity (B) Heuristieken (v2.4)
        // ============================================================
        
        private static readonly Dictionary<int, (double minB, double typicalB, double maxB)> RegisterBRanges = new()
        {
            // Deep Bass (A0-B1, MIDI 21-35)
            { 21, (0.0003, 0.0008, 0.003) },
            { 35, (0.0003, 0.0008, 0.003) },
            
            // Bass (C2-B2, MIDI 36-47)
            { 36, (0.0002, 0.0005, 0.001) },
            { 47, (0.0002, 0.0005, 0.001) },
            
            // Tenor (C3-C4, MIDI 48-60)
            { 48, (0.0001, 0.0003, 0.0006) },
            { 60, (0.0001, 0.0003, 0.0006) },
            
            // Mid-High (C#4-C5, MIDI 61-72)
            { 61, (0.00005, 0.00015, 0.0003) },
            { 72, (0.00005, 0.00015, 0.0003) },
            
            // Treble (C#5-C6, MIDI 73-84)
            { 73, (0.00003, 0.0001, 0.0002) },
            { 84, (0.00003, 0.0001, 0.0002) },
            
            // High Treble (C#6-C8, MIDI 85-108)
            { 85, (0.00005, 0.00015, 0.0004) },
            { 108, (0.0001, 0.0003, 0.001) }
        };

        /// <summary>
        /// Haalt de typische inharmoniciteits-coëfficiënt (B) op voor een gegeven noot.
        /// Gebaseerd op empirische data (Fletcher & Rossing).
        /// </summary>
        public static double GetTypicalInharmonicity(int midiNote)
        {
            midiNote = Math.Max(21, Math.Min(108, midiNote));

            int[] boundaries = { 21, 35, 36, 47, 48, 60, 61, 72, 73, 84, 85, 108 };
            
            for (int i = 0; i < boundaries.Length - 1; i += 2)
            {
                int low = boundaries[i];
                int high = boundaries[i + 1];
                
                if (midiNote >= low && midiNote <= high)
                {
                    return RegisterBRanges[low].typicalB;
                }
            }

            return 0.0003; // Fallback
        }

        /// <summary>
        /// Geeft het verwachte bereik (min, max) van B voor validatie.
        /// </summary>
        public static (double min, double max) GetInharmonicityRange(int midiNote)
        {
            midiNote = Math.Max(21, Math.Min(108, midiNote));
            
            int[] boundaries = { 21, 35, 36, 47, 48, 60, 61, 72, 73, 84, 85, 108 };
            
            for (int i = 0; i < boundaries.Length - 1; i += 2)
            {
                int low = boundaries[i];
                int high = boundaries[i + 1];
                
                if (midiNote >= low && midiNote <= high)
                {
                    var range = RegisterBRanges[low];
                    return (range.minB, range.maxB);
                }
            }
            
            return (0.0001, 0.001);
        }
    }
}