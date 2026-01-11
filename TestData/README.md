# Test Data

Deze directory bevat testdata voor de Auris Piano Tuner applicatie.

## Aanbevolen Hardware Setup

**✅ Getest en aanbevolen:** Behringer ECM8000 + Behringer UMC202HD

### Waarom deze combinatie?

**1. Impedantie matching en gain staging**
- ECM8000 en UMC202HD zijn **ontworpen voor elkaar** (Behringer ecosystem)
- Correcte signaal levels zonder extreme gain aanpassingen
- Optimale signal-to-noise ratio

**2. Bewezen meetresultaten**
- **11 partialen** gedetecteerd in A4 test
- **0.02 Hz nauwkeurigheid** (bijna perfect)
- Duidelijke detectie van lage partialen (n=2,3,6,9)

**3. PreSonus Studio 24c vergelijking**
- Studio 24c vereiste **gain helemaal open** voor ECM8000
- Resultaat: slechts **7 partialen** gedetecteerd (vs 11 met UMC202HD)
- **Lage partialen verdwenen** in ruisvloer door slechte impedantie matching
- Conclusie: Duurdere interface ≠ betere resultaten zonder correcte matching

**⚠️ Belangrijk:** Gain helemaal open zetten is een duidelijk signaal van **impedantie mismatch**. Dit resulteert in:
- Hogere ruisvloer (slechtere SNR)
- Verlies van zwakke partialen
- Minder nauwkeurige metingen

### Hardware specificaties

**Behringer ECM8000**
- Type: Omnidirectionele meetmicrofoon
- Frequentie respons: 15 Hz - 20 kHz (±3 dB)
- Impedantie: 600 Ω
- Gevoeligheid: -60 dBV/Pa

**Behringer UMC202HD**
- Sample rates: 44.1, 48, 96 kHz (96 kHz aanbevolen)
- Mic preamps: MIDAS ontwerp
- Input impedantie: 3 kΩ (optimaal voor ECM8000)
- Dynamisch bereik: 108 dB

**PreSonus Studio 24c**
- Sample rates: 44.1, 48, 88.2, 96, 192 kHz
- XMAX preamps
- Input impedantie: Hoger (geoptimaliseerd voor studio condenser mics)
- Resultaat met ECM8000: **Suboptimaal** (gain staging probleem)

---

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

**⚠️ Gain staging issue:** Gain moest helemaal open voor ECM8000 → impedantie mismatch

**Piano specificaties:**
- Type: Console piano
- Afmeting: 107 cm
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
- **Significant minder partialen** gedetecteerd (7 vs 11 met UMC202HD) op dezelfde piano
- **Belangrijkste oorzaak**: Gain helemaal open nodig = impedantie mismatch tussen ECM8000 en Studio 24c
- Impedantie mismatch resulteert in:
  - Slechtere signal-to-noise ratio
  - Zwakkere partialen verdwijnen onder ruisvloer
  - Ontbrekende partialen: n=2, 3, 6, 9, 15 niet gedetecteerd
- Fundamentele frequentie 1.09 Hz lager, mogelijk door:
  - Ontstemming tussen metingen (20 minuten tijdsverschil)
  - Temperatuur/vochtigheid effecten
- Vergelijkbare fundamentaal amplitude en kwaliteit score (beide Groen)
- Wel n=7 gedetecteerd (niet door UMC202HD) - interessante afwijking

---

## Audio Interface Vergelijking

### Test Setup
Beide metingen uitgevoerd met identieke procedure:
- Noot: A4 (target 440 Hz)
- Methode: 1 snaar gemeten, 2 snaren gedempt
- Sample rate: 96000 Hz
- FFT size: 32768

### Vergelijkingstabel

**Gemeten op dezelfde Console piano (107 cm), 20 minuten tijdsverschil**

| Parameter | UMC202HD | Studio 24c | Verschil |
|-----------|----------|------------|----------|
| **Fundamentaal** | 439.98 Hz | 438.89 Hz | -1.09 Hz |
| **Afwijking van 440 Hz** | -0.02 Hz | -1.11 Hz | -1.09 Hz |
| **Amplitude fundamentaal** | 48.56 dB | 47.33 dB | -1.23 dB |
| **Aantal partialen** | 11 | 7 | -4 partialen |
| **Hoogste partiaal** | n=16 (7025 Hz) | n=16 (7125 Hz) | ~100 Hz |
| **Inharmoniciteit** | 1E-05 | 1E-05 | Gelijk |
| **Kwaliteit** | Groen | Groen | Gelijk |

### Observaties

**✓ Gecontroleerde vergelijking:** Beide metingen uitgevoerd op **dezelfde Console piano** (107 cm) met 20 minuten tijdsverschil. Dit is een echte A/B test van de twee audio interfaces.

**Partiaal detectie patronen:**
- **UMC202HD** detecteerde meer opeenvolgende **lage partialen** (n=1,2,3,4,6,9)
- **Studio 24c** detecteerde **n=7** (niet door UMC202HD gedetecteerd)
- **Studio 24c** miste n=2,3,6,9,15 (mogelijk net onder detectiedrempel)
- Beide detecteerden dezelfde **hoge partialen** (n=11,12,13,16)

**Frequentie verschil (1.09 Hz):**
- UMC202HD: 439.98 Hz (zeer dicht bij 440 Hz)
- Studio 24c: 438.89 Hz (1.11 Hz lager)
- Mogelijke oorzaken:
  - Ontstemming gedurende 20 minuten meetinterval
  - Temperatuur/vochtigheid effecten op snaarspanning
  - Verschillende FFT bin interpolatie tussen interfaces

**Amplitude en kwaliteit:**
- Vergelijkbare fundamentaal amplitude: 48.56 dB vs 47.33 dB (1.23 dB verschil)
- Beide scoren "Groen" (goede kwaliteit)
- Beide meten identieke inharmoniciteit (1E-05)

**Interface-specifieke kenmerken:**
- UMC202HD lijkt gevoeliger voor lage partialen (n=2,3,6,9)
- Studio 24c detecteerde uniek n=7 (3124 Hz met 23.7 dB amplitude)
- Mogelijk verschil in frequentie response of ADC karakteristieken

**🔍 Gain staging verklaring (belangrijk):**
- **Studio 24c vereiste gain "helemaal open"** voor ECM8000 → impedantie mismatch
- **UMC202HD had normale gain instellingen** → correcte impedantie matching
- Resultaat van impedantie mismatch:
  - Zwakkere partialen verdwijnen onder ruisvloer (n=2,3,6,9,15 gemist)
  - Slechtere signal-to-noise ratio
  - Dit verklaart waarom duurdere interface **minder** partialen detecteert
- **Conclusie:** ECM8000 is ontworpen voor Behringer interfaces (600 Ω → 3 kΩ matching)

### Gebruiksdoeleinden

Deze testdata kan gebruikt worden voor:
- **Interface vergelijking**: A/B test tussen budget (UMC202HD) en mid-range (Studio 24c) interfaces
- **Validatie van FFT-analyse**: Testen of algoritmes consistent werken met verschillende hardware
- **Partiaal detectie**: Onderzoeken van detectiedrempels en gevoeligheid
- **Referentie metingen**: Enkelvoudige snaar metingen voor benchmark
- **Meetkwaliteit**: Both interfaces leveren "Groen" kwaliteit, maar met verschillende partiaal patronen
- **Reproducibiliteit**: Analyseren van frequentie stabiliteit over 20 minuten
- **Hardware specificaties**: Testen van impact van verschillende ADC karakteristieken

### Conclusies

1. **Beide interfaces leveren goede resultaten** (Groen kwaliteit score)
2. **UMC202HD detecteert significant meer partialen** (11 vs 7), vooral in lage frequenties
3. **Gain staging is cruciaal**: Studio 24c vereiste gain helemaal open = impedantie mismatch met ECM8000
4. **Impedantie matching verklaart verschil**: UMC202HD (3 kΩ) perfect voor ECM8000 (600 Ω)
5. **Frequentie verschil van 1.09 Hz** suggereert lichte ontstemming tijdens 20 minuten meetinterval
6. **Partiaal detectie verschilt aanzienlijk**: Studio 24c mist n=2,3,6,9,15 door slechte SNR
7. **Hardware aanbeveling**: ECM8000 + UMC202HD is de **optimale combinatie** voor deze applicatie
8. **Duurdere ≠ beter**: Studio 24c is een uitstekende interface, maar **niet geoptimaliseerd** voor ECM8000
9. **Voor piano tuning: gebruik UMC202HD met ECM8000** voor beste partiaal detectie en nauwkeurigheid
