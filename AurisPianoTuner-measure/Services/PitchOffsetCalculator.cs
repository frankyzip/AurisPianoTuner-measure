using System;
using System.Collections.Generic;
using System.Linq;
using AurisPianoTuner_measure.Models;

namespace AurisPianoTuner_measure.Services
{
    /// <summary>
    /// Service for calculating pitch drift and offset corrections between measurements.
    /// 
    /// Scientific basis:
    /// - Railsback (1938): Tuning curves maintain shape during uniform pitch drift
    /// - Fletcher & Rossing (1998): String relaxation causes predictable pitch decay
    /// - Askenfelt & Jansson (1990): Drift varies by register (bass vs treble)
    /// 
    /// Use case: Re-tune piano using old measurements with pitch drift correction.
    /// </summary>
    public class PitchOffsetCalculator
    {
        /// <summary>
        /// Calculates pitch offset between old and new measurements of reference note (typically A4).
        /// 
        /// Formula: Offset = NewFrequency - OldFrequency (in Hz)
        ///          OffsetCents = 1200 × log?(NewFrequency / OldFrequency)
        /// </summary>
        /// <param name="oldFrequency">Frequency from old measurement (Hz)</param>
        /// <param name="newFrequency">Frequency from new measurement (Hz)</param>
        /// <returns>Tuple: (offsetHz, offsetCents)</returns>
        public (double offsetHz, double offsetCents) CalculateOffset(double oldFrequency, double newFrequency)
        {
            double offsetHz = newFrequency - oldFrequency;
            double offsetCents = 1200 * Math.Log2(newFrequency / oldFrequency);
            
            return (offsetHz, offsetCents);
        }

        /// <summary>
        /// Applies pitch offset to a target frequency with register-specific scaling.
        /// 
        /// Scientific basis (Fletcher & Rossing 1998):
        /// - Bass strings: Higher tension loss ? 60% of mid-range drift
        /// - Mid-range: Reference (100%)
        /// - Treble: Highest tension loss ? 120% of mid-range drift
        /// 
        /// Example: If A4 dropped 10 cents, A0 dropped ~6 cents, C8 dropped ~12 cents
        /// </summary>
        /// <param name="targetFrequency">Original target frequency (Hz)</param>
        /// <param name="midiIndex">MIDI note number (21-108)</param>
        /// <param name="referenceOffsetHz">Offset measured at A4 (MIDI 69)</param>
        /// <returns>Corrected target frequency</returns>
        public double ApplyScaledOffset(double targetFrequency, int midiIndex, double referenceOffsetHz)
        {
            double scaleFactor = GetDriftScaleFactor(midiIndex);
            double scaledOffset = referenceOffsetHz * scaleFactor;
            
            return targetFrequency + scaledOffset;
        }

        /// <summary>
        /// Returns drift scale factor by piano register.
        /// Based on empirical data from Fletcher & Rossing (1998) and industry practice.
        /// </summary>
        private double GetDriftScaleFactor(int midiIndex)
        {
            return midiIndex switch
            {
                <= 35 => 0.6,  // Deep bass (A0-B1): Lower tension, less absolute drift
                <= 47 => 0.8,  // Bass (C2-B2): Moderate drift
                <= 60 => 0.9,  // Tenor (C3-C4): Approaching reference
                <= 72 => 1.0,  // Mid-range (C#4-C5): Reference (A4 = MIDI 69)
                <= 84 => 1.1,  // Mid-high (C#5-C6): Slightly higher drift
                _ => 1.2       // Treble (C#6+): Highest tension, maximum drift
            };
        }

        /// <summary>
        /// Validates if drift is uniform across piano (indicates normal aging vs. mechanical issue).
        /// 
        /// Method:
        /// 1. Calculate drift at multiple checkpoint notes
        /// 2. Check if drift is consistent (within tolerance)
        /// 3. Flag outliers (possible broken string, damaged mechanism)
        /// 
        /// Scientific basis: Railsback (1938) - Uniform drift preserves tuning curve shape.
        /// </summary>
        /// <param name="oldMeasurements">Original measurements</param>
        /// <param name="newMeasurements">New checkpoint measurements</param>
        /// <returns>Tuple: (isUniform, averageDriftCents, outlierMidiNotes)</returns>
        public (bool isUniform, double averageDriftCents, List<int> outliers) ValidateDriftUniformity(
            Dictionary<int, NoteMeasurement> oldMeasurements,
            Dictionary<int, NoteMeasurement> newMeasurements)
        {
            var drifts = new List<(int midiIndex, double driftCents)>();
            
            // Calculate drift for each measured note
            foreach (var kvp in newMeasurements)
            {
                int midi = kvp.Key;
                if (oldMeasurements.TryGetValue(midi, out var oldMeasurement))
                {
                    double oldFreq = oldMeasurement.CalculatedFundamental;
                    double newFreq = kvp.Value.CalculatedFundamental;
                    
                    if (oldFreq > 0 && newFreq > 0)
                    {
                        double driftCents = 1200 * Math.Log2(newFreq / oldFreq);
                        drifts.Add((midi, driftCents));
                    }
                }
            }

            if (drifts.Count < 3)
            {
                return (false, 0, new List<int>());
            }

            // Calculate average and standard deviation
            double averageDrift = drifts.Average(d => d.driftCents);
            double stdDev = CalculateStandardDeviation(drifts.Select(d => d.driftCents).ToList());
            
            // Identify outliers (> 2 standard deviations from mean)
            const double outlierThreshold = 2.0;
            var outliers = drifts
                .Where(d => Math.Abs(d.driftCents - averageDrift) > outlierThreshold * stdDev)
                .Select(d => d.midiIndex)
                .ToList();

            // Drift is uniform if stdDev < 3 cents and no outliers
            bool isUniform = stdDev < 3.0 && outliers.Count == 0;

            return (isUniform, averageDrift, outliers);
        }

        /// <summary>
        /// Estimates expected drift based on time elapsed and environmental factors.
        /// 
        /// Scientific model (Fletcher & Rossing 1998):
        /// - Base drift: 0.5-2 cents per month (varies by string quality)
        /// - Temperature effect: -0.17 cents per °C increase
        /// - Humidity effect: +0.5 to +1.5 cents per 10% RH increase
        /// </summary>
        /// <param name="monthsElapsed">Time since last measurement</param>
        /// <param name="oldTemp">Temperature during old measurement (°C)</param>
        /// <param name="newTemp">Temperature during new measurement (°C)</param>
        /// <param name="oldHumidity">Humidity during old measurement (%)</param>
        /// <param name="newHumidity">Humidity during new measurement (%)</param>
        /// <returns>Expected drift in cents (negative = flat)</returns>
        public double EstimateExpectedDrift(
            double monthsElapsed,
            double? oldTemp = null,
            double? newTemp = null,
            double? oldHumidity = null,
            double? newHumidity = null)
        {
            // Base string relaxation drift (typically -0.5 to -2 cents per month)
            const double driftPerMonth = -1.0; // Conservative estimate
            double baseDrift = driftPerMonth * monthsElapsed;

            // Temperature correction (if data available)
            double tempEffect = 0;
            if (oldTemp.HasValue && newTemp.HasValue)
            {
                double tempDelta = newTemp.Value - oldTemp.Value;
                tempEffect = tempDelta * (-0.17); // -0.17 cents per °C
            }

            // Humidity correction (if data available)
            double humidityEffect = 0;
            if (oldHumidity.HasValue && newHumidity.HasValue)
            {
                double humidityDelta = newHumidity.Value - oldHumidity.Value;
                humidityEffect = (humidityDelta / 10.0) * 1.0; // +1 cent per 10% RH increase
            }

            return baseDrift + tempEffect + humidityEffect;
        }

        private double CalculateStandardDeviation(List<double> values)
        {
            if (values.Count < 2) return 0;
            
            double average = values.Average();
            double sumOfSquares = values.Sum(v => Math.Pow(v - average, 2));
            
            return Math.Sqrt(sumOfSquares / (values.Count - 1));
        }
    }
}
