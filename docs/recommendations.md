# Recommendations: tags, sound analysis and a reference recommender

Tapedeck writes three extra Vorbis comments into every `.opus` file. They give a music player on the phone
everything it needs to play **similar songs**: a radio that starts from one song, or autoplay that keeps the
music going when a queue ends. This document is the contract for these fields and a guide to building a
recommender on top of them, the way the reference implementation does it.

- [The idea](#the-idea)
- [The tags](#the-tags)
  - [`TAPEDECK_TAGS`](#tapedeck_tags-what-listeners-call-the-artist)
  - [`TAPEDECK_HISTORY`](#tapedeck_history-the-spotify-history-of-the-song)
  - [`TAPEDECK_AUDIO`](#tapedeck_audio-how-the-song-sounds)
- [How Tapedeck measures the sound](#how-tapedeck-measures-the-sound)
- [The recommender](#the-recommender)
  - [Features and similarity](#features-and-similarity)
  - [Signal and taste profile](#signal-and-taste-profile)
  - [Picking the songs](#picking-the-songs)
  - [The rolling radio](#the-rolling-radio)
- [Checking and tuning](#checking-and-tuning)

## The idea

There is one listener and a library of a few hundred to a few thousand songs. Collaborative filtering ("people
who liked this also liked") needs many users, so the recommender is **content-based**: it compares songs by what
they are and how they sound, and learns the listener's taste from plays and skips.

Three design rules run through everything:

1. **Every field is optional.** A song without tags, without a year or without Tapedeck's fields is compared on
   what it has. A missing feature never counts against a song, so the player works with any library, and gets
   better the more Tapedeck has written.
2. **Deterministic, no trained model.** Everything is recomputed per request from the library, the listening data
   and the skips. A few thousand songs take a few milliseconds; the same data always gives the same result.
3. **First fit, then vibe.** Genre, artist and tags decide which songs may come at all. The sound decides the
   order among them: a calm ballad after a calm ballad, a driving track after a driving one.

Tapedeck writes tags rather than a sidecar file because Android 13 and later only let a player with
`READ_MEDIA_AUDIO` read audio files, not a `.json` next to them.

## The tags

All three are plain Vorbis comments in the Opus file, next to the usual ones; players that don't know them ignore
them. A field without data is **removed**, never written empty. Numbers use `.` as the decimal separator, no
thousands separator. Parse leniently: skip an entry you can't read and keep the others, never fail on a file.

Tapedeck changes these fields when the data behind them changes; the files are updated with **Write tags (N)**.
Read them again whenever a file's modification time changes.

### `TAPEDECK_TAGS`: what listeners call the artist

```
TAPEDECK_TAGS=synthwave=0.82;chill=0.40;female vocalists=0.31
```

The [Last.fm](https://www.last.fm) tags of the song's artist, with their weight.

- Entries are separated by `;`, name and weight by `=`. The name is everything before the **last** `=`.
- Weight = Last.fm's count / 100, exactly two decimals, `0.00`–`1.00`. Strongest first, ties by name.
- At most 15 tags, each with a count of at least 10. Names are lowercase with single spaces; `;` and `=` inside a
  name become spaces.
- Tags about the listener rather than the music are left out: `seen live`, `favorites`, `albums i own`,
  `under 2000 listeners` and similar.
- An artist without tags of their own ("A feat. B") gets those of the main artist.

These are artist tags, so all songs of an artist share them. They carry moods and scenes that no genre list
has: `melancholic`, `90s`, `female vocalists`, `gangsta rap`.

### `TAPEDECK_HISTORY`: the Spotify history of the song

```
TAPEDECK_HISTORY=plays=57;skips=3;last=2025-03-14
```

What the listener's Spotify streaming history says about the song, when they took it over in Tapedeck.

| Key | Meaning |
|---|---|
| `plays` | Real plays: at least 30 s and not ended with the forward or back button |
| `skips` | Early skips: ended with the forward button within the first 10 s |
| `last` | Day (UTC, `yyyy-MM-dd`) of the latest play or early skip; may be missing |

Only raw counts: how much old plays still count is up to the player (see
[Signal and taste profile](#signal-and-taste-profile)). The field is removed when both counts are 0.

> [!IMPORTANT]
> The history is **not a feature for similarity**. Two songs are not alike because both were played 50 times. It
> is a measure of how much the listener likes a song, and it changes the score at the end.

### `TAPEDECK_AUDIO`: how the song sounds

```
TAPEDECK_AUDIO=v=1;bpm=123.4;bpmconf=0.62;centroid=2140/610;flatness=0.081/0.034;flux=0.112/0.051;rolloff=4810/1320;zcr=0.0612/0.0223;rms=-14.2/3.1;crest=11.8/2.0
```

Tempo, timbre, energy and dynamics, measured from the audio. Every value except the tempo is given as
`mean/spread`: the mean over the song and its standard deviation. A small spread means the song stays the same
throughout, a large one that it changes, like a quiet verse and a loud chorus or a build-up and a drop.

| Key | Unit | Meaning | Typical range |
|---|---|---|---|
| `v` | | Format version. **Ignore the field when you don't know the version.** | `1` |
| `bpm` | beats per minute | Tempo, 60–200. May be half or double the felt tempo, see below | 60–200 |
| `bpmconf` | 0–1 | How clearly the beat repeats. Below ~0.3 the tempo is a guess (ballads, free rhythm) | 0.17–0.92 |
| `centroid` | Hz | Spectral centroid: how **bright** the song sounds | 1000 (piano) – 3600 (party) |
| `flatness` | 0–1 | Noise and distortion (1 = white noise) against clear tones (0 = sine) | 0.00–0.10 |
| `flux` | 0–~1.4 | How much the spectrum changes from frame to frame: how **busy** the arrangement is | 0.21–0.59 |
| `rolloff` | Hz | Frequency below which 85 % of the energy lies, a coarser brightness | 300–3000 |
| `zcr` | per sample | Zero-crossing rate, a third brightness and noise measure | 0.03–0.18 |
| `rms` | dBFS | Frame level: **energy**. 0 is full scale | −23 to −5 |
| `crest` | dB | Peak to RMS per second: **dynamics**. Low for heavily compressed tracks | 8–17 |

The ranges are the extremes of a real library of 1145 songs. Values are raw physical units on purpose: a player
compares them **relative to its own library** (see [Features and similarity](#features-and-similarity)), so the
format stays the same when the weighting changes.

A file with less than a few seconds of sound has no `TAPEDECK_AUDIO`.

## How Tapedeck measures the sound

You don't need this to use the tags, but it tells you what the numbers can and can't say. The code is in
`Tapedeck.Core/Audio/`.

**Decoding.** FFmpeg decodes the file once to mono PCM at 22,050 Hz (at most 15 minutes). Everything else is
plain C#, with a small radix-2 FFT and a Hann window.

**What counts.** Intro and outro are cut, 10 % at each end but at most 20 s, and only for songs longer than a
minute, so verse and chorus decide. Frames quieter than −50 dBFS (fades, pauses) don't count.

**Timbre, energy and dynamics.** Frames of 2048 samples (~93 ms), every 1024 samples:

| Value | Per frame |
|---|---|
| Centroid | Σ f·\|X(f)\| / Σ \|X(f)\| over the magnitude spectrum, without the DC bin |
| Flatness | geometric mean / arithmetic mean of the power spectrum |
| Flux | √Σ max(0, x_t(f) − x_{t−1}(f))² over the magnitude spectra normalized to length 1, so a change of the sound counts and a change of the level does not |
| Rolloff | lowest frequency below which 85 % of the power lies |
| ZCR | sign changes / (frame length − 1) |
| RMS | 20·log10 of the frame's RMS |
| Crest | 20·log10(peak / RMS) per block of one second |

**Tempo.** An onset curve shows when something new starts: the rise of the compressed spectrum `ln(1 + 10·|X|)`
per frame of 1024 samples, every 256 samples (~86 per second), minus the mean of the surrounding half second,
lightly smoothed. Its autocorrelation shows which period repeats. For every period between 60 and 200 BPM:

```
score(P) = (r(P) + 0.5·r(2P) + 0.5·r(4P)) · exp(−½·(log2(bpm / 115))²)
```

- **Harmonic enhancement** ([Percival & Tzanetakis, 2014](https://dblp.uni-trier.de/rec/journals/taslp/PercivalT14.html)):
  the true beat also repeats two and four beats later, a triplet or off-beat pattern does not. This removes most
  errors of 2:3 and 3:4.
- **Tempo preference**: a log-normal prior around 115 BPM, one octave wide, as in
  [librosa](https://librosa.org/doc/0.10.2/generated/librosa.feature.tempo.html).
- The period is refined to a fraction of a frame from the peak four periods later.
- `bpmconf` is r(P) / r(0).

**Felt tempo.** Ballads with steady eighth notes come out at double tempo: *Imagine* at 152 instead of 76. So a
song measured at 120 BPM or more that sounds **calm** (flux mean below 0.35) and has **no steady beat**
(`bpmconf` below 0.65) gets the half tempo. Calm dance music with a clear beat, like *A Sky Full of Stars*, keeps
its tempo.

**How good is it?** On 98 songs of a real library with known tempo: 81 exact, 12 at half or double, 5 at 2:3 or
3:4. Ten ballads were measured at double tempo before the felt-tempo rule, three are now.

> [!NOTE]
> Which octave is "the" tempo is often a matter of taste: trap is felt at half its notated tempo, and *Blinding
> Lights* is 171 or 85.5 depending on whom you ask. Compare tempos with half and double time in mind (see below).

> [!WARNING]
> **RMS and crest depend a lot on the decade.** Older records are mastered quieter and more dynamic: in the same
> library, songs from the 1980s average −13.7 dBFS with a crest of 13.8 dB, songs from the 2010s −9.3 dBFS and
> 10.8 dB. *Eye of the Tiger* measures as quiet and dynamic as a ballad. Keep their weight moderate, or the era
> counts twice.

**Versions.** `v` in the tag is the format version and only changes when the fields change. Tapedeck also keeps
the version of the measurement: when the algorithm improves, it measures every file again in the background and
writes the same format with better numbers.

## The recommender

### Features and similarity

Every song is described in **blocks**. Each block is compared on its own, and the similarity of two songs is the
weighted mean over the blocks **both** songs have:

```
similarity(a, b) = Σ w_k · sim_k(a, b) / Σ w_k        over the blocks a and b both have
```

| Block | Weight | Source | What it does |
|---|---:|---|---|
| **Sound** | **0.60** | `TAPEDECK_AUDIO` | The mood: energy, tempo, brightness, density |
| Tags | 0.14 | `TAPEDECK_TAGS` | Moods and scenes listeners name |
| Main genre | 0.12 | genre tag | A coarse guard rail |
| Artist | 0.08 | artist, album artist | A slight preference for the same artist |
| Era | 0.04 | year | Production era |
| Length | 0.02 | duration | Separates interludes and radio edits from extended mixes |

The metadata blocks keep the ratios of a recommender without sound (0.35 : 0.30 : 0.20 : 0.10 : 0.05),
scaled to 0.40.

**Metadata blocks** are sparse vectors, each normalized to length 1, compared by cosine:

- **Main genre**: the first main genre 1.0, a second one 0.6.
- **Artist**: the main artist 1.0, co-artists 0.7, the album artist 0.5 (not "Various Artists").
- **Era**: the half-decade 1.0, its neighbors 0.5 and 0.15, so 1999 and 2001 are close but not equal.
- **Length**: buckets at 2:30, 4:30 and 7:00, the neighbors 0.3.
- **Tags**: the weights from `TAPEDECK_TAGS`.

**The sound block** is compared relative to the library. For every value and every song, take the robust z-score

```
z = (value − median) / (1.4826 · MAD)        median and MAD over all songs of the library
```

and compare two songs per value with a Gaussian kernel `exp(−½·(z_a − z_b)²)`: 1 for equal, 0.61 one typical
spread apart, 0.14 two spreads apart. Hz, dB and ratios become comparable this way, and the library decides what
"far" means. The values are weighted like this, each 70 % on the mean and 30 % on the spread:

| Value | Weight | Why |
|---|---:|---|
| Centroid | 0.11 | Brightness, the main factor of how alike two songs sound |
| Flatness | 0.09 | Distorted guitars and noise against clear tones |
| RMS | 0.08 | Energy, keeps quiet acoustic songs away from loud ones |
| Crest | 0.08 | Dynamics |
| Tempo | 0.14 | Keeps the pulse of the radio steady |
| Flux | 0.06 | Calm pads against busy, complex beats |
| Rolloff | 0.02 | Mostly the same as the centroid, so it stays low |
| ZCR | 0.02 | Same |

The tempo is compared in octaves with a fixed spread of 0.1 octaves, also as half or double time:

```
tempo(a, b) = max( k(log2(a/b)), 0.85 · k(log2(a/b) − 1), 0.85 · k(log2(a/b) + 1) )       k(d) = exp(−½·(d / 0.1)²)
```

120 against 128 BPM gives 0.65, against 160 almost 0, against 60 gives 0.85. When either song has `bpmconf`
below 0.3, the tempo counts with half its weight. A song without a tempo (`bpm` 0) leaves the tempo out.

> [!TIP]
> Tapedeck has the same formula in `Tapedeck.Core/Audio/AudioSimilarity.cs`. Keep both in step if you change it.

### Signal and taste profile

Every song gets a **signal**: how much the listener likes it.

```
signal = ln(1 + plays·d₉₀ + spotifyPlays·d₇₃₀) + 0.5·favorite + 0.3·listening·d₉₀ − Σ skips − 0.6·spotifySkips·d₇₃₀
```

- `d₉₀` and `d₇₃₀` halve the weight every 90 days (the player's own data) or every two years (the Spotify history,
  which reaches back years), each from the last play.
- `listening` is the song's total listening time divided by the largest in the library, 0–1.
- A **skip** is a track change the listener made (not a track ending by itself), weighted by how early it came:
  under 3 s 1.0, under 6 s 0.6, under 10 s 0.3. Inside a radio it counts 1.5 times. Going back to the song within
  5 s takes the skip back. Each skip fades with its own age (90 days). A Spotify skip counts like the middle
  level.
- A song with two skips under 3 s in the last 30 days is **left out** of every radio.

The **taste profile** is a Rocchio combination of the feature vectors: the mean of the liked songs (signal > 0,
weighted by the signal) minus half the mean of the disliked ones. For the sound block, take the weighted mean of
the liked songs' z-scores and leave the tempo out: averaging the tempos of many songs means little.

### Picking the songs

**Who may come.** Not the seeds, not what is queued, not what was played in the last two hours, not a blocked
song, and with a list as the source (see [The rolling radio](#the-rolling-radio)), only songs of that list. Of
those, only songs whose **metadata similarity** to the seeds is at least **0.25**. The metadata similarity is the
formula above without the sound block. So the sound never brings an off-topic song in; it only orders.

**The score** of a candidate:

```
score = (0.75 · similarity(seeds, song) + 0.25 · similarity(profile, song)) · (1 + 0.15 · tanh(signal))
```

The seeds set the direction, the profile reorders within it, and the signal is a multiplier of 0.85–1.15: songs
the listener loves come a little more often, songs they skip a little less, without ever pushing a song that
doesn't fit. Without a profile (no listening data yet), the seed part alone counts.

With several seeds of different weight, compare the metadata against their weighted combination, and the sound
against each seed: the weighted mean of the sound similarities to the positive seeds. A combined tempo of two
songs would mean little.

**Drawing.** A radio has close and exploring places.

- **Close places** draw from the best 3·N candidates by score, with a softmax of temperature 0.05: weight
  `exp((score − best) / 0.05)`, so a song 0.05 better comes about e times as often.
- **Every 4th place explores**: a song with a metadata similarity of at least 0.40, but clearly (0.05) below every
  close candidate, drawn with a flatter softmax (temperature 0.15). A bit further away, never off-topic.
- **Artist variety**: never the current song's artist right next, and at most two songs per artist among the new
  songs and the last three played.
- The random draw uses a seed from the day and the current song, so a radio is stable for a day and testable.
- When too few songs fit (a small list, one artist), the caps are relaxed, and at last songs of the seeds' artists
  and albums fill up.

### The rolling radio

A radio is not a fixed list. It keeps **10 songs ahead** and recomputes them on **every** track change, so each
play and skip counts right away. Songs the listener queued by hand stay where they are.

| Seed | Weight |
|---|---:|
| The current song | 1.0 |
| The song before it | 0.5 |
| The song the radio started from (the anchor) | 0.3 |
| The last three songs skipped in this radio | −0.5 each |

A skipped seed pushes the metadata away from itself (and is left out of the sound part).

- **Radio** ("play similar songs" from a song menu): starts at a song, the anchor keeps it near there.
- **Radio from a list**: tapping a song in an album, genre or playlist recommends only songs of that list.
- **Autoplay**: when the last song of a queue starts, a radio begins without an anchor and may wander with what
  plays.

## Checking and tuning

Tapedeck has a test bench for the sound analysis. It measures every song without a value and writes
`audio-report.md` into the app data folder: how the values spread over the library, and for some random songs the
ten that sound most alike, by sound alone:

```bash
dotnet run --project Tapedeck -- --audio-report 20
```

Good signs: ballads find ballads, bangers find bangers, and the farthest songs of the same genre are the opposite
mood. If the neighbors are the same decade rather than the same mood, lower the weight of RMS and crest.
