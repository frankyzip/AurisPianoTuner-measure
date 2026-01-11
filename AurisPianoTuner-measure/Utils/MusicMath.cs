using System;

namespace AurisPianoTuner_measure.Utils
{
    /// <summary>
    /// Utility-klasse voor noot- en frequentie-berekeningen.
    /// Wetenschappelijke basis: Equal temperament en MIDI standaard.
    /// </summary>
    public static class MusicMath
    {
        /// <summary>
        /// Standaard A4 frequentie (440 Hz) volgens internationale standaard.
        /// </summary>
        public const double A4_FREQUENCY = 440.0;
        
        /// <summary>
        /// MIDI noot nummer voor A4.
        /// </summary>
        public const int A4_MIDI = 69;

        /// <summary>
        /// Nootnamen voor display doeleinden.
        /// </summary>
        private static readonly string[] NoteNames = {
            "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"
        };

        /// <summary>
        /// Berekent theoretische frequentie uit MIDI noot nummer.
        /// Formule: f = 440 × 2^((n-69)/12)
        /// </summary>
        /// <param name="midiNote">MIDI noot nummer (21-108 voor piano)</param>
        /// <returns>Theoretische frequentie in Hz</returns>
        public static double MidiToFrequency(int midiNote)
        {
            return A4_FREQUENCY * Math.Pow(2.0, (midiNote - A4_MIDI) / 12.0);
        }

        /// <summary>
        /// Berekent MIDI noot nummer uit frequentie.
        /// Formule: n = 69 + 12 × log?(f/440)
        /// </summary>
        /// <param name="frequency">Frequentie in Hz</param>
        /// <returns>MIDI noot nummer (kan decimaal zijn)</returns>
        public static double FrequencyToMidi(double frequency)
        {
            return A4_MIDI + 12.0 * Math.Log2(frequency / A4_FREQUENCY);
        }

        /// <summary>
        /// Converteert MIDI noot naar leesbare nootnaam.
        /// </summary>
        /// <param name="midiNote">MIDI noot nummer</param>
        /// <returns>Nootnaam met octaaf (bijv. "A4", "C#3")</returns>
        public static string MidiToNoteName(int midiNote)
        {
            int noteIndex = midiNote % 12;
            int octave = midiNote / 12 - 1;
            return NoteNames[noteIndex] + octave.ToString();
        }

        /// <summary>
        /// Berekent cent-afwijking tussen twee frequenties.
        /// Formule: cents = 1200 × log?(f?/f?)
        /// </summary>
        /// <param name="actualFreq">Gemeten frequentie</param>
        /// <param name="targetFreq">Theoretische frequentie</param>
        /// <returns>Afwijking in cents (positief = te hoog, negatief = te laag)</returns>
        public static double FrequencyToCents(double actualFreq, double targetFreq)
        {
            if (targetFreq <= 0 || actualFreq <= 0)
                return 0;
            
            return 1200.0 * Math.Log2(actualFreq / targetFreq);
        }

        /// <summary>
        /// Berekent frequentie uit cent-afwijking.
        /// Formule: f? = f? × 2^(cents/1200)
        /// </summary>
        /// <param name="baseFreq">Basis frequentie</param>
        /// <param name="cents">Cent-afwijking</param>
        /// <returns>Resulterende frequentie</returns>
        public static double CentsToFrequency(double baseFreq, double cents)
        {
            return baseFreq * Math.Pow(2.0, cents / 1200.0);
        }

        /// <summary>
        /// Controleert of een MIDI noot een witte toets is op een piano.
        /// </summary>
        /// <param name="midiNote">MIDI noot nummer</param>
        /// <returns>True als witte toets</returns>
        public static bool IsWhiteKey(int midiNote)
        {
            int noteInOctave = midiNote % 12;
            // Witte toetsen: C(0), D(2), E(4), F(5), G(7), A(9), B(11)
            return noteInOctave == 0 || noteInOctave == 2 || noteInOctave == 4 ||
                   noteInOctave == 5 || noteInOctave == 7 || noteInOctave == 9 ||
                   noteInOctave == 11;
        }

        /// <summary>
        /// Berekent het register (deel van piano) voor een MIDI noot.
        /// </summary>
        /// <param name="midiNote">MIDI noot nummer</param>
        /// <returns>Register naam</returns>
        public static string GetRegister(int midiNote)
        {
            return midiNote switch
            {
                <= 35 => "Diepe bas",      // A0-B1
                <= 47 => "Bas",           // C2-B2  
                <= 60 => "Tenor",         // C3-C4
                <= 72 => "Middenbereik",  // C#4-C5
                <= 84 => "Hoog",          // C#5-C6
                _ => "Discant"            // C#6-C8
            };
        }
    }
}