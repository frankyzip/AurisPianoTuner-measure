using AurisPianoTuner_measure.Models;
using System;

namespace AurisPianoTuner_measure.Services
{
    public interface IFftAnalyzerService
    {
        // Property om te checken of de meting klaar is
        bool IsMeasurementLocked { get; }

        void ProcessAudioBuffer(float[] samples);
        void SetTargetNote(int midiIndex, double theoreticalFrequency);
        void SetPianoMetadata(PianoMetadata metadata);

        event EventHandler<NoteMeasurement> MeasurementUpdated;
        event EventHandler<FftSpectrumData> RawSpectrumUpdated;

        // NEW: Event voor de automatische stop
        event EventHandler<NoteMeasurement> MeasurementAutoStopped;

        void Reset();
    }
}
