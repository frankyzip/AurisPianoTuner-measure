using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using AurisPianoTuner_measure.Models;

namespace AurisPianoTuner_measure.Services
{
    public class MeasurementStorageService : IMeasurementStorageService
    {
        private readonly JsonSerializerOptions _jsonOptions;

        public MeasurementStorageService()
        {
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never,
                Converters = { new JsonStringEnumConverter() }
            };
        }

        public async Task SaveMeasurementsAsync(string filePath, Dictionary<int, NoteMeasurement> measurements, PianoMetadata? pianoMetadata = null)
        {
            try
            {
                // Converteer naar een serializeerbaar formaat
                var data = new MeasurementFileData
                {
                    Version = "1.1",
                    CreatedAt = DateTime.Now,
                    SampleRate = 96000,
                    FftSize = 32768,
                    PianoMetadata = pianoMetadata ?? new PianoMetadata(),
                    Measurements = new List<NoteMeasurement>(measurements.Values)
                };

                string json = JsonSerializer.Serialize(data, _jsonOptions);
                await File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new Exception($"Fout bij opslaan metingen: {ex.Message}", ex);
            }
        }

        public async Task<(Dictionary<int, NoteMeasurement> measurements, PianoMetadata? metadata)> LoadMeasurementsAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException($"Bestand niet gevonden: {filePath}");
                }

                string json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
                var data = JsonSerializer.Deserialize<MeasurementFileData>(json, _jsonOptions);

                if (data == null || data.Measurements == null)
                {
                    throw new Exception("Ongeldig bestandsformaat");
                }

                // Converteer terug naar dictionary
                var result = new Dictionary<int, NoteMeasurement>();
                foreach (var measurement in data.Measurements)
                {
                    result[measurement.MidiIndex] = measurement;
                }

                return (result, data.PianoMetadata);
            }
            catch (Exception ex)
            {
                throw new Exception($"Fout bij laden metingen: {ex.Message}", ex);
            }
        }

        private class MeasurementFileData
        {
            public string Version { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
            public int SampleRate { get; set; }
            public int FftSize { get; set; }
            public PianoMetadata? PianoMetadata { get; set; }
            public List<NoteMeasurement> Measurements { get; set; } = new();
        }
    }
}
