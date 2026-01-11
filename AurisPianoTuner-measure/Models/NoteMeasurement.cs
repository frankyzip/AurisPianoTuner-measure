using System.Collections.Generic;
using System;

namespace AurisPianoTuner_measure.Models
{
    public class NoteMeasurement
    {
        public string NoteName { get; set; } = string.Empty;
        public int MidiIndex { get; set; }
        public double TargetFrequency { get; set; }
        public List<PartialResult> DetectedPartials { get; set; } = new();
        public string Quality { get; set; } = string.Empty; // Groen, Oranje, Rood
        
        /// <summary>
        /// De partial die gebruikt is voor de meting (register-afhankelijk).
        /// Bijvoorbeeld: n=1 voor hoge noten, n=6 voor lage basnoten.
        /// </summary>
        public int MeasuredPartialNumber { get; set; } = 1;
        
        /// <summary>
        /// De werkelijke fundamentele frequentie, berekend uit de gemeten partial.
        /// Formula: TrueFundamental = MeasuredPartial.Frequency / MeasuredPartialNumber
        /// </summary>
        public double CalculatedFundamental { get; set; }
        
        /// <summary>
        /// Inharmoniciteitscoëfficiënt (B) berekend uit de gedetecteerde partials.
        /// Wetenschappelijke basis: Fletcher & Rossing (1998), "The Physics of Musical Instruments", p.362-364
        /// 
        /// Formule: f_n = n·f?·?(1 + B·n²)
        /// 
        /// Typische waarden:
        /// - Concert Grand (280cm): B ? 0.00003 - 0.0002
        /// - Baby Grand (150-170cm): B ? 0.0001 - 0.0004
        /// - Console (110-120cm): B ? 0.0003 - 0.0008
        /// - Spinet (<110cm): B ? 0.0005 - 0.002
        /// 
        /// Opmerking: B varieert sterk per register (hoger in bass door kortere/zwaardere snaren).
        /// </summary>
        public double InharmonicityCoefficient { get; set; } = 0.0;
        
        public DateTime MeasuredAt { get; set; } = DateTime.Now;
    }

    public class PartialResult
    {
        public int n { get; set; } // Partieel nummer
        public double Frequency { get; set; }
        public double Amplitude { get; set; }
    }
}
