# `settings.json`

Settings of the agent, written by the dashboard and read by the agent at start. The file sits next to the database: `%LOCALAPPDATA%\KeyStats\settings.json` for an installed agent, or the folder of the `--db` database otherwise. It holds executable names and thresholds only, never typed text or window titles.

## Format

UTF-8 JSON, keys in camelCase. Comments (`//`, `/* */`) and trailing commas are accepted, so the file can be edited by hand.

```json
{
  "version": 1,
  "excludedApps": ["KeePass.exe", "KeePassXC.exe", "Bitwarden.exe"],
  "analysis": {
    "sessionGapMs": 5000,
    "activeGapMs": 2000,
    "minNetChars": 30
  },
  "liveCounter": { "enabled": true },
  "backupFolder": null
}
```

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `version` | integer | `1` | Format version. A file with a higher version is refused. |
| `excludedApps` | array of strings, `null` or absent | built-in list | Executables for which only the key count is kept: no raw data, no title, no text analysis. Matched on the file name, case-insensitive. An array replaces the built-in list, and an empty array excludes nothing; `null`, as the dashboard writes it, is the same as an absent key. |
| `analysis` | object | see below | Analysis thresholds. |
| `liveCounter.enabled` | boolean | `true` | Shows the live counter. |
| `backupFolder` | absolute path, or `null` | `null` | Folder of the nightly mirror; `null` means `backup\` next to the database. |

`analysis` takes the properties of `AnalysisSettings` (`src/KeyStats.Core/Analysis/AnalysisSettings.cs`), whose comments give their meaning. Bounds are inclusive.

| Key | Default | Min | Max |
| --- | --- | --- | --- |
| `sessionGapMs` | 5000 | 1 | 600000 |
| `activeGapMs` | 2000 | 1 | `sessionGapMs` |
| `minNetChars` | 30 | 0 | 100000, and at most `maxSessionChars` |
| `minSpaces` | 3 | 0 | 100000, and at most `maxSessionChars` |
| `minDurationMs` | 8000 | 1 | 3600000 |
| `minTextRatio` | 0.75 | 0 | 1 |
| `minDistinctChars` | 10 | 0 | 1000 |
| `maxRepeatRatio` | 0.3 | 0 | 1 |
| `proseMaxSymbolRatio` | 0.03 | 0 | `codeMinSymbolRatio` |
| `codeMinSymbolRatio` | 0.08 | 0 | 1 |
| `peakWindowMs` | 30000 | 1 | 120000 |
| `holdCapMs` | 2000 | 1 | 10000 |
| `bigramMaxLatencyMs` | 1000 | 1 | 10000 |
| `fakeCtrlMs` | 5 | 0 | 100 |
| `maxCorrectionLength` | 40 | 1 | 128 (half the correction buffer) |
| `maxSessionChars` | 20000 | 1 | 100000 |
| `liveShowMinChars` | 12 | 0 | 1000 |
| `liveShowMinSpaces` | 2 | 0 | 1000 |
| `liveShowWindowMs` | 6000 | 1 | 30000 |
| `liveSpeedWindowMs` | 8000 | 1 | 30000 |

## Rules

- An absent key keeps its default; an unknown key is ignored, so later additions within version 1 stay readable by an older agent.
- The whole file is refused, and the defaults apply, when it is not valid JSON, is larger than 1 MiB, has a value of the wrong type (numbers are never read from strings), or a value out of bounds: an `analysis` value outside the table above, a blank or `null` name in `excludedApps`, a relative `backupFolder`, a `null` `analysis` or `liveCounter`. A refused file is never rewritten by the agent.
- The dashboard writes the file to `settings.json.tmp` first, then moves it over `settings.json`: a reader sees either the old file or the new one.
