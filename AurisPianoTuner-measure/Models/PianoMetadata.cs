namespace AurisPianoTuner_measure.Models
{
    /// <summary>
    /// Piano metadata for measurement context and future tuning curve calculation.
    /// Based on scientific literature:
    /// - Fletcher & Rossing (1998): "The Physics of Musical Instruments"
    /// - Conklin (1996): "Design and Tone in the Mechanoacoustic Piano"
    /// - Askenfelt & Jansson (1990): "Five Lectures on the Acoustics of the Piano"
    /// 
    /// Piano dimensions directly affect:
    /// 1. Inharmonicity coefficient (B) - small pianos have higher B values
    /// 2. Scale break location - transition from wound bass to unwound tenor strings
    /// 3. Partial detection accuracy - shorter strings require wider FFT search windows
    /// 4. Railsback stretch curve - concert grands require less stretch than spinets
    /// </summary>
    public class PianoMetadata
    {
        /// <summary>
        /// Type of piano, categorized by size and string length characteristics.
        /// Scientific basis: String length directly determines inharmonicity (Conklin 1996).
        /// </summary>
        public PianoType Type { get; set; } = PianoType.Unknown;

        /// <summary>
        /// Physical dimension in centimeters.
        /// For uprights: Height from floor to top
        /// For grands: Length from keyboard to tail
        /// </summary>
        public int DimensionCm { get; set; } = 0;

        /// <summary>
        /// MIDI note number where bass section ends and tenor begins.
        /// This marks the physical transition from wound (copper-wrapped) strings to plain steel strings.
        /// 
        /// Typical range: E2 (MIDI 40) to F2 (MIDI 42) for standard pianos.
        /// Extended range: Up to F#3 (MIDI 54) for non-standard designs.
        /// 
        /// Scientific basis: Askenfelt & Jansson (1990) - "Five Lectures on the Acoustics of the Piano"
        /// 
        /// Physical characteristics at scale break:
        /// - Wound strings (below break): Copper wire wrapped around steel core
        ///   ? Higher mass per unit length ? Higher inharmonicity (B)
        /// - Plain strings (above break): Single steel wire
        ///   ? Lower mass per unit length ? Lower inharmonicity (B)
        /// 
        /// This transition often exhibits:
        /// - Abrupt change in inharmonicity coefficient (factor 2-4 reduction)
        /// - Sudden increase in partial detectability
        /// - Tonal color shift from "warm/mellow" to "bright/clear"
        /// 
        /// Example: Fazer Console 107cm has scale break at E3-F3 (MIDI 52-53):
        /// - E3 (MIDI 52): Last copper-wound string, B ? 800×10??
        /// - F3 (MIDI 53): First plain steel string, B ? 300×10??
        /// </summary>
        public int ScaleBreakMidiNote { get; set; } = 41; // Default: F2

        /// <summary>
        /// Optional: Date and time when measurement was taken.
        /// Useful for tracking drift over time.
        /// </summary>
        public DateTime? MeasurementDateTime { get; set; }

        /// <summary>
        /// Optional: Serial number for tracking individual instruments.
        /// </summary>
        public string SerialNumber { get; set; } = string.Empty;

        /// <summary>
        /// Date of last tuning (if known).
        /// </summary>
        public string? LastTuningDate { get; set; }

        /// <summary>
        /// Free-form notes about the instrument condition, environment, etc.
        /// </summary>
        public string Notes { get; set; } = string.Empty;
    }

    /// <summary>
    /// Piano type classification based on scientific literature and industry standards.
    /// Categories defined by string length ranges which determine inharmonicity characteristics.
    /// 
    /// References:
    /// - Fletcher & Rossing (1998): Chapter 10 - Piano Acoustics
    /// - Conklin (1996): Table 1 - Inharmonicity vs. Piano Size
    /// </summary>
    public enum PianoType
    {
        Unknown = 0,

        // === UPRIGHT PIANOS ===
        /// <summary>
        /// Spinet: Height &lt; 110cm
        /// Characteristics: Very short strings, highest inharmonicity (B ? 500-2000 × 10??)
        /// Common in compact living spaces, significant tuning stretch required.
        /// </summary>
        Spinet = 1,

        /// <summary>
        /// Console/Studio: 110-120cm
        /// Characteristics: Medium string length, moderate inharmonicity (B ? 300-800 × 10??)
        /// Standard home piano, balanced tone vs. size compromise.
        /// </summary>
        Console = 2,

        /// <summary>
        /// Professional Upright: 120-135cm
        /// Characteristics: Longer strings, lower inharmonicity (B ? 200-500 × 10??)
        /// Professional/institutional use, approaching grand piano tone quality.
        /// </summary>
        ProfessionalUpright = 3,

        // === GRAND PIANOS ===
        /// <summary>
        /// Baby Grand: 150-170cm
        /// Characteristics: Horizontal strings, moderate inharmonicity (B ? 150-400 × 10??)
        /// Home grand piano, improved tone over uprights due to longer strings.
        /// </summary>
        BabyGrand = 4,

        /// <summary>
        /// Parlor/Medium Grand: 170-210cm
        /// Characteristics: Medium-long strings, low inharmonicity (B ? 100-300 × 10??)
        /// Studio/small venue instrument, professional recording quality.
        /// </summary>
        ParlorGrand = 5,

        /// <summary>
        /// Semi-Concert Grand: 210-250cm
        /// Characteristics: Long strings, very low inharmonicity (B ? 50-200 × 10??)
        /// Teaching studios, small concert halls, excellent tonal balance.
        /// </summary>
        SemiConcertGrand = 6,

        /// <summary>
        /// Concert Grand: &gt; 250cm (typically 274-280cm)
        /// Characteristics: Very long strings, minimal inharmonicity (B ? 30-150 × 10??)
        /// Concert hall standard (Steinway Model D, Bösendorfer 280, Yamaha CFX)
        /// Minimal tuning stretch required, most harmonically pure tone.
        /// </summary>
        ConcertGrand = 7
    }
}
