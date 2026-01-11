using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.Wave;
using NAudio.Wave.Asio;
using AurisPianoTuner_measure.Models;

namespace AurisPianoTuner_measure.Services
{
    public class AsioAudioService : IAudioService, IDisposable
    {
        private AsioOut? _asioOut;
        public bool IsRunning { get; private set; }
        public event EventHandler<float[]>? AudioDataAvailable;

        public IEnumerable<string> GetAsioDrivers()
        {
            return AsioOut.GetDriverNames();
        }

        public void Start(string driverName, int sampleRate)
        {
            if (IsRunning) Stop();

            try
            {
                _asioOut = new AsioOut(driverName);

                // Pre-check: Valideer of de driver de gevraagde sample rate ondersteunt
                // NAudio's AsioOut.InitRecordAndPlayback gooit een exception als sample rate niet ondersteund wordt
                _asioOut.InputChannelOffset = 0; // Kanaal 1 van de interface (Mic in)
                
                // Initialiseer recording (1 input kanaal) - dit valideert automatisch de sample rate
                try
                {
                    _asioOut.InitRecordAndPlayback(null, 1, sampleRate);
                }
                catch (Exception ex)
                {
                    _asioOut?.Dispose();
                    _asioOut = null;
                    throw new NotSupportedException(
                        $"De ASIO driver '{driverName}' ondersteunt de sample rate van {sampleRate} Hz niet. " +
                        $"Controleer of uw audio-interface (bijv. UMC202HD) correct is geconfigureerd op 96 kHz in het ASIO control panel. " +
                        $"Fout: {ex.Message}", ex);
                }

                _asioOut.AudioAvailable += OnAudioAvailable;
                
                // Start de ASIO engine met Play() (NAudio's API voor zowel playback als recording)
                _asioOut.Play();
                IsRunning = true;
            }
            catch (NotSupportedException)
            {
                throw; // Re-throw our custom exception
            }
            catch (Exception ex)
            {
                _asioOut?.Dispose();
                _asioOut = null;
                throw new InvalidOperationException(
                    $"Fout bij starten van ASIO driver '{driverName}': {ex.Message}", ex);
            }
        }

        public void ShowControlPanel()
        {
            _asioOut?.ShowControlPanel();
        }

        private void OnAudioAvailable(object? sender, AsioAudioAvailableEventArgs e)
        {
            // Dit is de "hot path". Hier komen de samples binnen op 96kHz.
            // We converteren ze naar een float array voor makkelijke berekeningen.
            float[] samples = new float[e.SamplesPerBuffer];
            
            // We lezen alleen het eerste kanaal (uw ECM8000 mic)
            e.GetAsInterleavedSamples(samples);

            // Stuur de data door naar wie er luistert (de analyzer)
            AudioDataAvailable?.Invoke(this, samples);
        }

        public void Stop()
        {
            if (_asioOut != null)
            {
                _asioOut.Stop();
                _asioOut.Dispose();
                _asioOut = null;
            }
            IsRunning = false;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
