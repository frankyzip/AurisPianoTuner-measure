using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AurisPianoTuner_measure.Models;

namespace AurisPianoTuner_measure.Services
{
    public interface ITestLoggerService
    {
        void LogAnalysisAttempt(int midiNote, List<PartialResult> partials, double rmsVolume);
        Task SaveSessionLogAsync(string pianoName);
    }

    public class TestLoggerService : ITestLoggerService
    {
        private readonly List<string> _logEntries = new();
        private readonly string _logDirectory = "TestLogs";

        public TestLoggerService()
        {
            if (!Directory.Exists(_logDirectory)) Directory.CreateDirectory(_logDirectory);
            _logEntries.Add("Timestamp;MidiNote;VolumeRMS;PartialIndex;Frequency;Amplitude");
        }

        public void LogAnalysisAttempt(int midiNote, List<PartialResult> partials, double rmsVolume)
        {
            string ts = DateTime.Now.ToString("HH:mm:ss.fff");
            foreach (var p in partials)
            {
                // Use existing PartialResult fields: n, Frequency, Amplitude
                _logEntries.Add($"{ts};{midiNote};{rmsVolume:F6};{p.n};{p.Frequency:F4};{p.Amplitude:F6}");
            }
        }

        public async Task SaveSessionLogAsync(string pianoName)
        {
            if (_logEntries.Count <= 1) return;

            string safeName = string.IsNullOrWhiteSpace(pianoName) ? "UnknownPiano" : string.Join('_', pianoName.Split(Path.GetInvalidFileNameChars()));
            string fileName = $"DebugLog_{safeName}_{DateTime.Now:yyyyMMdd_HHmm}.csv";
            string path = Path.Combine(_logDirectory, fileName);
            try
            {
                await File.WriteAllLinesAsync(path, _logEntries, Encoding.UTF8).ConfigureAwait(false);
                _logEntries.Clear(); // Reset for next session
                _logEntries.Add("Timestamp;MidiNote;VolumeRMS;PartialIndex;Frequency;Amplitude");
            }
            catch
            {
                // Ignore logging errors during tests
            }
        }
    }
}
