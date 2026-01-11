# Test Data

Deze directory bevat testdata voor de Auris Piano Tuner applicatie.

## AudioMeasurements

Bevat audio meetresultaten in JSON formaat (versie 1.1) zoals gegenereerd door de `MeasurementStorageService`.

### A4_SingleString_Console_440Hz.json

**Beschrijving:** Meting van A4 (440 Hz) met één snaar, twee andere snaren gedempt.

**Piano specificaties:**
- Type: Console piano
- Afmeting: 107 cm
- Scale break: MIDI note 41

**Meetgegevens:**
- Sample rate: 96000 Hz
- FFT size: 32768
- Gemeten op: 2026-01-11T22:03:43
- Kwaliteit: Groen (goed)

**Resultaten:**
- Fundamentele frequentie: 439.98 Hz (afwijking: -0.014 Hz van 440 Hz target)
- Inharmoniciteitscoëfficiënt: 1E-05 (zeer laag, goed voor één snaar)
- Gedetecteerde partialen: 11 (n=1, 2, 3, 4, 6, 9, 11, 12, 13, 15, 16)
- Sterkste partiaal: n=1 (fundamentaal) met amplitude 48.56 dB

**Opmerkingen:**
Deze meting toont uitstekende resultaten voor een enkele snaar:
- Zeer accurate frequentiemeting (< 0.05 Hz afwijking)
- Duidelijke harmonische structuur met 11 gedetecteerde partialen
- Lage inharmoniciteit zoals verwacht bij één snaar
- Hoogste partialen tot 7 kHz gedetecteerd

Deze testdata kan gebruikt worden voor:
- Validatie van de FFT-analyse algoritmes
- Testen van partiaal detectie
- Referentie voor enkelvoudige snaar metingen
- Benchmark voor meetkwaliteit
