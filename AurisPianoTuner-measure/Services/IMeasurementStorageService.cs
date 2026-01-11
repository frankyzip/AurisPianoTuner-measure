using System.Collections.Generic;
using System.Threading.Tasks;
using AurisPianoTuner_measure.Models;

namespace AurisPianoTuner_measure.Services
{
    public interface IMeasurementStorageService
    {
        Task SaveMeasurementsAsync(string filePath, Dictionary<int, NoteMeasurement> measurements, PianoMetadata? pianoMetadata = null);
        Task<(Dictionary<int, NoteMeasurement> measurements, PianoMetadata? metadata)> LoadMeasurementsAsync(string filePath);
    }
}
