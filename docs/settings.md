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
| `excludedApps` | array of strings, or absent | built-in list | Executables for which only the key count is kept: no raw data, no title, no text analysis. Matched on the file name, case-insensitive. When present, the array replaces the built-in list; an empty array excludes nothing. |
| `analysis` | object | see below | Analysis thresholds. |
| `liveCounter.enabled` | boolean | `true` | Shows the live counter. |
| `backupFolder` | absolute path, or `null` | `null` | Folder of the nightly mirror; `null` means `backup\` next to the database. |

`analysis` takes the properties of `AnalysisSettings` (`src/KeyStats.Core/Analysis/AnalysisSettings.cs`), each with the default documented there: `sessionGapMs`, `activeGapMs`, `minNetChars`, `minSpaces`, `minDurationMs`, `minTextRatio`, `minDistinctChars`, `maxRepeatRatio`, `proseMaxSymbolRatio`, `codeMinSymbolRatio`, `peakWindowMs`, `holdCapMs`, `bigramMaxLatencyMs`, `fakeCtrlMs`, `maxCorrectionLength`, `maxSessionChars`, `liveShowMinChars`, `liveShowMinSpaces`, `liveShowWindowMs`, `liveSpeedWindowMs`.

## Rules

- An absent key keeps its default; an unknown key is ignored, so later additions within version 1 stay readable by an older agent.
- The whole file is refused, and the defaults apply, when it is not valid JSON, is larger than 1 MiB, has a value of the wrong type (numbers are never read from strings), or a value out of bounds: durations and window sizes strictly positive, counts not negative, ratios within [0, 1], `activeGapMs` at most `sessionGapMs`, `proseMaxSymbolRatio` at most `codeMinSymbolRatio`, no blank name in `excludedApps`, `backupFolder` absolute. A refused file is never rewritten by the agent.
- The dashboard writes the file to `settings.json.tmp` first, then moves it over `settings.json`: a reader sees either the old file or the new one.
