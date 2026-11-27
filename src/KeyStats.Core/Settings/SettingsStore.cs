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

    /// <summary>Ne lève jamais, chemin vide compris : un fichier refusé donne les valeurs par défaut, sans être réécrit.</summary>
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
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return Invalid();
        }
    }

    /// <summary>
    /// Écrit dans un fichier temporaire puis le met à la place : un lecteur, ou le disque après une coupure, voit l'ancien fichier ou le nouveau, jamais un fichier tronqué.
    /// Un seul écrivain, le tableau de bord. En cas d'échec, le temporaire est supprimé.
    /// </summary>
    public static void Save(string path, KeyStatsSettings settings)
    {
        if (!IsValid(settings)) throw new ArgumentException("Réglages hors bornes.", nameof(settings));
        string tmp = path + ".tmp";
        try
        {
            using (var file = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                file.Write(JsonSerializer.SerializeToUtf8Bytes(settings, SettingsJson.Default.KeyStatsSettings));
                // Données sur le disque avant le renommage : après une coupure, l'ancien fichier ou le nouveau complet.
                file.Flush(flushToDisk: true);
            }
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            // L'erreur d'origine compte : un temporaire impossible à supprimer ne la masque pas.
            try { File.Delete(tmp); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    private static SettingsLoad Invalid() => new(KeyStatsSettings.Default, SettingsStatus.Invalid);

    internal static bool IsValid(KeyStatsSettings s) =>
        s.Version is >= 1 and <= KeyStatsSettings.CurrentVersion
        && s.Analysis is { } a && IsValid(a)
        && s.LiveCounter is { } live && live.IsValid()
        && (s.ExcludedApps is null || s.ExcludedApps.All(e => !string.IsNullOrWhiteSpace(e)))
        && (s.BackupFolder is null || Path.IsPathFullyQualified(s.BackupFolder));

    /// <summary>
    /// Bornes incluses de chaque seuil entier, reprises dans <c>docs/settings.md</c>. Les bornes hautes tiennent
    /// les tableaux dimensionnés par un seuil (correction), les anneaux à taille fixe qu'une fenêtre doit couvrir (pointe,
    /// compteur en direct) et les conversions en <c>short</c> (latences) dans leurs limites.
    /// </summary>
    internal static readonly (string Key, Func<AnalysisSettings, int> Get, int Min, int Max)[] IntBounds =
    [
        ("sessionGapMs", a => a.SessionGapMs, 1, 600_000),
        ("activeGapMs", a => a.ActiveGapMs, 1, 600_000),
        ("minNetChars", a => a.MinNetChars, 0, 100_000),
        ("minSpaces", a => a.MinSpaces, 0, 100_000),
        ("minDurationMs", a => a.MinDurationMs, 1, 3_600_000),
        ("minDistinctChars", a => a.MinDistinctChars, 0, 1_000),
        ("peakWindowMs", a => a.PeakWindowMs, 1, 120_000),
        ("holdCapMs", a => a.HoldCapMs, 1, 10_000),
        ("bigramMaxLatencyMs", a => a.BigramMaxLatencyMs, 1, 10_000),
        ("fakeCtrlMs", a => a.FakeCtrlMs, 0, 100),
        ("maxCorrectionLength", a => a.MaxCorrectionLength, 1, CorrectionAnalyzer.BufferSize / 2),
        ("maxSessionChars", a => a.MaxSessionChars, 1, 100_000),
        ("liveShowMinChars", a => a.LiveShowMinChars, 0, 1_000),
        ("liveShowMinSpaces", a => a.LiveShowMinSpaces, 0, 1_000),
        ("liveShowWindowMs", a => a.LiveShowWindowMs, 1, 30_000),
        ("liveSpeedWindowMs", a => a.LiveSpeedWindowMs, 1, 30_000),
    ];

    private static bool IsValid(AnalysisSettings a) =>
        IntBounds.All(b => b.Get(a) >= b.Min && b.Get(a) <= b.Max)
        && a.ActiveGapMs <= a.SessionGapMs
        // Sinon aucune session, coupée avant, ne compterait jamais comme de l'écriture.
        && a.MinNetChars <= a.MaxSessionChars && a.MinSpaces <= a.MaxSessionChars
        && Ratio(a.MinTextRatio) && Ratio(a.MaxRepeatRatio) && Ratio(a.ProseMaxSymbolRatio) && Ratio(a.CodeMinSymbolRatio)
        && a.ProseMaxSymbolRatio <= a.CodeMinSymbolRatio;

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
