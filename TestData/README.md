# Test Data

Deze directory bevat testdata voor de Auris Piano Tuner applicatie.

## AudioMeasurements

Bevat audio meetresultaten in JSON formaat (versie 1.1) zoals gegenereerd door de `MeasurementStorageService`.

### A4_SingleString_Console_440Hz.json

**Beschrijving:** Meting van A4 (440 Hz) met één snaar, twee andere snaren gedempt.

**Audio interface:** Behringer UMC202HD

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
- Fundamentele frequentie: 439.98 Hz (afwijking: -0.02 Hz van 440 Hz target)
- Inharmoniciteitscoëfficiënt: 1E-05 (zeer laag, goed voor één snaar)
- Gedetecteerde partialen: 11 (n=1, 2, 3, 4, 6, 9, 11, 12, 13, 15, 16)
- Sterkste partiaal: n=1 (fundamentaal) met amplitude 48.56 dB

**Opmerkingen:**
Deze meting toont uitstekende resultaten voor een enkele snaar:
- Zeer accurate frequentiemeting (< 0.05 Hz afwijking)
- Duidelijke harmonische structuur met 11 gedetecteerde partialen
- Lage inharmoniciteit zoals verwacht bij één snaar
- Hoogste partialen tot 7 kHz gedetecteerd

---

### A4_SingleString_Studio24c_440Hz.json

**Beschrijving:** Meting van A4 (440 Hz) met één snaar, twee andere snaren gedempt.

**Audio interface:** PreSonus Studio 24c

**Piano specificaties:**
- Type: Parlor Grand
- Afmeting: 180 cm
- Scale break: MIDI note 41

**Meetgegevens:**
- Sample rate: 96000 Hz
- FFT size: 32768
- Gemeten op: 2026-01-11T22:23:22
- Kwaliteit: Groen (goed)

**Resultaten:**
- Fundamentele frequentie: 438.89 Hz (afwijking: -1.11 Hz van 440 Hz target)
- Inharmoniciteitscoëfficiënt: 1E-05 (zeer laag, goed voor één snaar)
- Gedetecteerde partialen: 7 (n=1, 4, 7, 11, 12, 13, 16)
- Sterkste partiaal: n=1 (fundamentaal) met amplitude 47.33 dB

**Opmerkingen:**
- Minder partialen gedetecteerd (7 vs 11 met UMC202HD)
- Fundamentele frequentie iets lager, mogelijk door:
  - Verschillende piano (Console vs Parlor Grand volgens metadata)
  - Ontstemming tussen metingen
  - 20 minuten tijdsverschil tussen metingen
- Vergelijkbare amplitude en kwaliteit score (beide Groen)
- Ontbrekende partialen: n=2, 3, 6, 9, 15 niet gedetecteerd

---

## Audio Interface Vergelijking

### Test Setup
Beide metingen uitgevoerd met identieke procedure:
- Noot: A4 (target 440 Hz)
- Methode: 1 snaar gemeten, 2 snaren gedempt
- Sample rate: 96000 Hz
- FFT size: 32768

### Vergelijkingstabel

| Parameter | UMC202HD (Console) | Studio 24c (Parlor Grand) | Verschil |
|-----------|-------------------|---------------------------|----------|
| **Fundamentaal** | 439.98 Hz | 438.89 Hz | -1.09 Hz |
| **Afwijking van 440 Hz** | -0.02 Hz | -1.11 Hz | -1.09 Hz |
| **Amplitude fundamentaal** | 48.56 dB | 47.33 dB | -1.23 dB |
| **Aantal partialen** | 11 | 7 | -4 partialen |
| **Hoogste partiaal** | n=16 (7025 Hz) | n=16 (7125 Hz) | ~100 Hz |
| **Inharmoniciteit** | 1E-05 | 1E-05 | Gelijk |
| **Kwaliteit** | Groen | Groen | Gelijk |

### Observaties

**⚠️ Belangrijk:** De piano metadata verschilt tussen beide metingen (Console 107cm vs Parlor Grand 180cm), wat suggereert dat dit mogelijk verschillende piano's of verschillende instellingen zijn. Dit beïnvloedt de vergelijkbaarheid.

**Partiaal detectie:**
- UMC202HD detecteerde meer opeenvolgende lage partialen (n=1,2,3,4,6)
- Studio 24c miste n=2,3,6,9,15 maar detecteerde wel n=7 (niet door UMC202HD gedetecteerd)
- Beide detecteerden hogere partialen (n=11,12,13,16)

**Mogelijke verklaringen voor verschillen:**
1. **Verschillende piano's** (volgens metadata): Console vs Parlor Grand kunnen andere harmonische karakteristieken hebben
2. **Ontstemming**: 20 minuten tussen metingen, temperatuur/vochtigheid effecten
3. **Peak detectie gevoeligheid**: Mogelijk net andere amplitudes rond detectiedrempel
4. **Microfoon positie**: Kleine verschillen in positie beïnvloeden partiaal balans

### Gebruiksdoeleinden

Deze testdata kan gebruikt worden voor:
- Validatie van de FFT-analyse algoritmes
- Testen van partiaal detectie onder verschillende condities
- Referentie voor enkelvoudige snaar metingen
- Benchmark voor meetkwaliteit
- Vergelijking van audio interface prestaties
- Testen van robuustheid tegen verschillende piano types
