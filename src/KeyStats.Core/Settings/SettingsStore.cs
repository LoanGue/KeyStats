using System.Text.Json;
using System.Text.Json.Serialization;
using KeyStats.Core.Analysis;

namespace KeyStats.Core.Settings;

public enum SettingsStatus
{
    /// <summary>Fichier lu et valide.</summary>
    Loaded,
    /// <summary>Pas de fichier : valeurs par défaut.</summary>
    Missing,
    /// <summary>Fichier illisible, mal formé, trop gros, d'une version future ou hors bornes : valeurs par défaut, fichier laissé tel quel.</summary>
    Invalid,
}

public readonly record struct SettingsLoad(KeyStatsSettings Settings, SettingsStatus Status);

/// <summary>Lecture et écriture de <c>settings.json</c>, par sérialisation générée à la compilation (NativeAOT).</summary>
public static class SettingsStore
{
    public const string FileName = "settings.json";
    internal const int MaxBytes = 1 << 20;

    /// <summary>À côté de la base.</summary>
    public static string PathFor(string dbPath) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath))!, FileName);

    /// <summary>Ne lève jamais : un fichier refusé donne les valeurs par défaut, sans être réécrit.</summary>
    public static SettingsLoad Load(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return new(KeyStatsSettings.Default, SettingsStatus.Missing);
            if (info.Length > MaxBytes) return Invalid();
            var settings = JsonSerializer.Deserialize(File.ReadAllBytes(path), SettingsJson.Default.KeyStatsSettings);
            return settings is not null && IsValid(settings) ? new(settings, SettingsStatus.Loaded) : Invalid();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return Invalid();
        }
    }

    /// <summary>Écrit dans un fichier temporaire puis le met à la place : un lecteur voit l'ancien fichier ou le nouveau, jamais un fichier tronqué.</summary>
    public static void Save(string path, KeyStatsSettings settings)
    {
        if (!IsValid(settings)) throw new ArgumentException("Réglages hors bornes.", nameof(settings));
        string tmp = path + ".tmp";
        File.WriteAllBytes(tmp, JsonSerializer.SerializeToUtf8Bytes(settings, SettingsJson.Default.KeyStatsSettings));
        File.Move(tmp, path, overwrite: true);
    }

    private static SettingsLoad Invalid() => new(KeyStatsSettings.Default, SettingsStatus.Invalid);

    internal static bool IsValid(KeyStatsSettings s) =>
        s.Version is >= 1 and <= KeyStatsSettings.CurrentVersion
        && s.Analysis is { } a && IsValid(a)
        && s.LiveCounter is not null
        && (s.ExcludedApps is null || s.ExcludedApps.All(e => !string.IsNullOrWhiteSpace(e)))
        && (s.BackupFolder is null || Path.IsPathFullyQualified(s.BackupFolder));

    private static bool IsValid(AnalysisSettings a) =>
        Positive(a.SessionGapMs, a.ActiveGapMs, a.MinDurationMs, a.PeakWindowMs, a.HoldCapMs, a.BigramMaxLatencyMs,
            a.MaxCorrectionLength, a.MaxSessionChars, a.LiveShowWindowMs, a.LiveSpeedWindowMs)
        && a.ActiveGapMs <= a.SessionGapMs
        && a is { MinNetChars: >= 0, MinSpaces: >= 0, MinDistinctChars: >= 0, FakeCtrlMs: >= 0, LiveShowMinChars: >= 0, LiveShowMinSpaces: >= 0 }
        && Ratio(a.MinTextRatio) && Ratio(a.MaxRepeatRatio) && Ratio(a.ProseMaxSymbolRatio) && Ratio(a.CodeMinSymbolRatio)
        && a.ProseMaxSymbolRatio <= a.CodeMinSymbolRatio;

    private static bool Positive(params ReadOnlySpan<int> values)
    {
        foreach (int v in values) if (v <= 0) return false;
        return true;
    }

    private static bool Ratio(double r) => r is >= 0 and <= 1;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    NumberHandling = JsonNumberHandling.Strict)]
[JsonSerializable(typeof(KeyStatsSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
