using AurisPianoTuner_measure.Models;
using System;

namespace AurisPianoTuner_measure.Services
{
    public interface IFftAnalyzerService
    {
        void ProcessAudioBuffer(float[] samples);
        void SetTargetNote(int midiIndex, double theoreticalFrequency);
        void SetPianoMetadata(PianoMetadata metadata);
        
        /// <summary>
        /// Event voor geprocesseerde metingen (gemiddeld, met partials).
        /// </summary>
        event EventHandler<NoteMeasurement> MeasurementUpdated;
        
        /// <summary>
        /// Event voor raw FFT spectrum data (real-time visualisatie).
        /// Scientific basis: Smith (2011) - real-time spectral monitoring
        /// Vuurt af bij elke FFT analyse voor live spectrum display.
        /// </summary>
        event EventHandler<FftSpectrumData> RawSpectrumUpdated;
        
        void Reset();
    }
}
