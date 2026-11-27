using System.Globalization;
using KeyStats.Core.Settings;

namespace KeyStats.Core.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("keystats-settings-").FullName;

    private string Path_ => Path.Combine(_dir, SettingsStore.FileName);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private SettingsLoad LoadText(string json)
    {
        File.WriteAllText(Path_, json);
        return SettingsStore.Load(Path_);
    }

    [Fact]
    public void Fichier_absent_valeurs_par_defaut()
    {
        var load = SettingsStore.Load(Path_);
        Assert.Equal(SettingsStatus.Missing, load.Status);
        Assert.Null(load.Settings.ExcludedApps);
        Assert.Same(Focus.ExclusionList.Default, load.Settings.Exclusions());
        Assert.Equal(Path_, SettingsStore.PathFor(Path.Combine(_dir, "keystats.db")));
    }

    [Fact]
    public void Cles_absentes_par_defaut_cles_inconnues_ignorees_commentaires_admis()
    {
        var load = LoadText("""
            {
              // Commentaire écrit à la main.
              "analysis": { "minSpaces": 5, "seuilFutur": 1 },
              "optionFuture": true,
            }
            """);
        Assert.Equal(SettingsStatus.Loaded, load.Status);
        Assert.Equal(5, load.Settings.Analysis.MinSpaces);
        Assert.Equal(30, load.Settings.Analysis.MinNetChars);
        Assert.Null(load.Settings.ExcludedApps);
        Assert.True(load.Settings.LiveCounter.Enabled);
    }

    [Fact]
    public void Liste_vide_aucune_appli_exclue()
    {
        var load = LoadText("""{ "excludedApps": [] }""");
        Assert.Equal(0, load.Settings.Exclusions().Count);
    }

    [Theory]
    [InlineData("{ pas du json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("""{ "version": 2 }""")]
    [InlineData("""{ "version": 0 }""")]
    [InlineData("""{ "analysis": null }""")]
    [InlineData("""{ "liveCounter": null }""")]
    [InlineData("""{ "liveCounter": { "position": "centre" } }""")]
    [InlineData("""{ "liveCounter": { "position": null } }""")]
    [InlineData("""{ "liveCounter": { "opacity": 10 } }""")]
    [InlineData("""{ "analysis": { "sessionGapMs": 0 } }""")]
    [InlineData("""{ "analysis": { "activeGapMs": 6000 } }""")]
    [InlineData("""{ "analysis": { "minNetChars": -1 } }""")]
    [InlineData("""{ "analysis": { "minTextRatio": 1.5 } }""")]
    [InlineData("""{ "analysis": { "proseMaxSymbolRatio": 0.2 } }""")]
    [InlineData("""{ "analysis": { "maxSessionChars": 20 } }""")]
    [InlineData("""{ "analysis": { "sessionGapMs": "5000" } }""")]
    [InlineData("""{ "excludedApps": ["KeePass.exe", " "] }""")]
    [InlineData("""{ "excludedApps": [null] }""")]
    [InlineData("""{ "backupFolder": "backup" }""")]
    public void Fichier_refuse_valeurs_par_defaut_et_fichier_intact(string json)
    {
        var load = LoadText(json);
        Assert.Equal(SettingsStatus.Invalid, load.Status);
        Assert.Equal(5000, load.Settings.Analysis.SessionGapMs);
        Assert.Null(load.Settings.ExcludedApps);
        Assert.Equal(json, File.ReadAllText(Path_));
    }

    public static TheoryData<string, int, int> Bornes()
    {
        var data = new TheoryData<string, int, int>();
        foreach (var b in SettingsStore.IntBounds) data.Add(b.Key, b.Min, b.Max);
        return data;
    }

    [Theory]
    [MemberData(nameof(Bornes))]
    public void Seuil_entier_accepte_aux_bornes_refuse_au_dela(string key, int min, int max)
    {
        // Écarts compatibles avec toutes les valeurs testées ; la clé testée les remplace.
        string Json(int v)
        {
            var fields = new Dictionary<string, int>
            {
                ["sessionGapMs"] = 600_000,
                ["activeGapMs"] = 1,
                ["maxSessionChars"] = 100_000,
                ["minNetChars"] = 0,
                ["minSpaces"] = 0,
                [key] = v,
            };
            return "{ \"analysis\": { " + string.Join(", ", fields.Select(f => $"\"{f.Key}\": {f.Value}")) + " } }";
        }
        Assert.Equal(SettingsStatus.Loaded, LoadText(Json(min)).Status);
        Assert.Equal(SettingsStatus.Loaded, LoadText(Json(max)).Status);
        Assert.Equal(SettingsStatus.Invalid, LoadText(Json(min - 1)).Status);
        Assert.Equal(SettingsStatus.Invalid, LoadText(Json(max + 1)).Status);
    }

    [Fact]
    public void Seuils_par_defaut_dans_les_bornes()
    {
        // Les 16 seuils entiers de AnalysisSettings, chacun borné.
        Assert.Equal(16, SettingsStore.IntBounds.Length);
        var defaults = Analysis.AnalysisSettings.Default;
        Assert.All(SettingsStore.IntBounds, b => Assert.InRange(b.Get(defaults), b.Min, b.Max));
    }

    [Fact]
    public void Chemin_vide_refuse_sans_lever()
    {
        Assert.Equal(SettingsStatus.Invalid, SettingsStore.Load("").Status);
    }

    [Fact]
    public void Fichier_trop_gros_refuse()
    {
        File.WriteAllText(Path_, "{" + new string(' ', SettingsStore.MaxBytes) + "}");
        Assert.Equal(SettingsStatus.Invalid, SettingsStore.Load(Path_).Status);
    }

    [Fact]
    public void Fichier_verrouille_refuse_sans_lever()
    {
        File.WriteAllText(Path_, "{}");
        using var hold = new FileStream(Path_, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Equal(SettingsStatus.Invalid, SettingsStore.Load(Path_).Status);
    }

    [Fact]
    public void Aller_retour_sans_perte_ni_fichier_temporaire()
    {
        var settings = new KeyStatsSettings
        {
            ExcludedApps = ["Signal.exe"],
            Analysis = new() { SessionGapMs = 4000, MinTextRatio = 0.6 },
            LiveCounter = new() { Enabled = false },
            BackupFolder = Path.Combine(_dir, "miroir"),
        };
        SettingsStore.Save(Path_, settings);
        Assert.Equal([SettingsStore.FileName], Directory.GetFiles(_dir).Select(Path.GetFileName));

        var load = SettingsStore.Load(Path_);
        Assert.Equal(SettingsStatus.Loaded, load.Status);
        Assert.Equal(["Signal.exe"], load.Settings.ExcludedApps!);
        Assert.Equal(4000, load.Settings.Analysis.SessionGapMs);
        Assert.Equal(0.6, load.Settings.Analysis.MinTextRatio);
        Assert.Equal(2000, load.Settings.Analysis.ActiveGapMs);
        Assert.False(load.Settings.LiveCounter.Enabled);
        Assert.Equal(settings.BackupFolder, load.Settings.BackupFolder);
        Assert.True(load.Settings.Exclusions().Contains(@"C:\Apps\signal.EXE"));
        Assert.False(load.Settings.Exclusions().Contains("KeePass.exe"));
    }

    [Fact]
    public void Noms_des_cles_du_format()
    {
        // Contrat lu par le tableau de bord : noms en camelCase.
        SettingsStore.Save(Path_, new KeyStatsSettings { ExcludedApps = [] });
        string json = File.ReadAllText(Path_);
        foreach (string key in new[] { "\"version\": 1", "\"excludedApps\": []", "\"analysis\"", "\"sessionGapMs\": 5000", "\"liveCounter\"", "\"enabled\": true", "\"position\": \"bottomRight\"", "\"opacity\": 100", "\"backupFolder\": null" })
            Assert.Contains(key, json);
    }

    [Fact]
    public void Ecriture_echouee_ne_laisse_pas_de_temporaire()
    {
        Directory.CreateDirectory(Path_);
        Assert.ThrowsAny<Exception>(() => SettingsStore.Save(Path_, KeyStatsSettings.Default));
        Assert.False(File.Exists(Path_ + ".tmp"));
    }

    [Fact]
    public void Reglages_hors_bornes_jamais_ecrits()
    {
        File.WriteAllText(Path_, "{}");
        var bad = new KeyStatsSettings { Analysis = new() { HoldCapMs = -1 } };
        Assert.Throws<ArgumentException>(() => SettingsStore.Save(Path_, bad));
        Assert.Equal("{}", File.ReadAllText(Path_));
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public void Table_des_bornes_de_la_doc_conforme_au_code()
    {
        string dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "docs", "settings.md"))) dir = Path.GetDirectoryName(dir)!;
        string doc = File.ReadAllText(Path.Combine(dir, "docs", "settings.md"));
        var defaults = Analysis.AnalysisSettings.Default;
        foreach (var b in SettingsStore.IntBounds)
        {
            string max = b.Key == "activeGapMs" ? "`sessionGapMs`" : b.Max.ToString(CultureInfo.InvariantCulture);
            Assert.Contains($"| `{b.Key}` | {b.Get(defaults)} | {b.Min} | {max}", doc);
        }
        (string Key, double Default, string Max)[] ratios =
        [
            ("minTextRatio", defaults.MinTextRatio, "1"),
            ("maxRepeatRatio", defaults.MaxRepeatRatio, "1"),
            ("proseMaxSymbolRatio", defaults.ProseMaxSymbolRatio, "`codeMinSymbolRatio`"),
            ("codeMinSymbolRatio", defaults.CodeMinSymbolRatio, "1"),
        ];
        foreach (var r in ratios)
            Assert.Contains($"| `{r.Key}` | {r.Default.ToString(CultureInfo.InvariantCulture)} | 0 | {r.Max} |", doc);

        // Aucune ligne de la table sans seuil dans le code : une clé retirée du code ne reste pas documentée.
        string table = doc[doc.IndexOf("| Key | Default | Min | Max |", StringComparison.Ordinal)..];
        var documented = table.Split('\n').TakeWhile(l => l.StartsWith('|')).Skip(2).Select(l => l.Split('`')[1]);
        var known = SettingsStore.IntBounds.Select(b => b.Key).Concat(ratios.Select(r => r.Key));
        Assert.Equal(known.Order(), documented.Order());
    }
}
