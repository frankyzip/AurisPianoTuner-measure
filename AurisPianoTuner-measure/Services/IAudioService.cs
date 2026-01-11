using System;
using System.Collections.Generic;
using AurisPianoTuner_measure.Models;

namespace AurisPianoTuner_measure.Services
{
    public interface IAudioService
    {
        IEnumerable<string> GetAsioDrivers();
        void Start(string driverName, int sampleRate);
        void Stop();
        void ShowControlPanel();
        event EventHandler<float[]> AudioDataAvailable;
        bool IsRunning { get; }
    }
}
