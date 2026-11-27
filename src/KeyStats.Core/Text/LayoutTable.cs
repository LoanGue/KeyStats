using System.Security.Cryptography;
using System.Text;
using KeyStats.Core.Input;
using KeyStats.Core.Storage;

namespace KeyStats.Core.Text;

/// <summary>Combinaison de modificateurs qui choisit le caractère d'une touche.</summary>
[Flags]
public enum ShiftState : byte
{
    None = 0,
    Shift = 1,
    AltGr = 2,
    Caps = 4,
}

/// <summary>
/// Ce que produit chaque touche d'une disposition clavier, pour chaque combinaison de Maj, AltGr
/// et Verr Maj, plus une case pour les chiffres du pavé avec Verr Num. Construite par l'agent à
/// partir de Windows, enregistrée dans la table <c>layout</c> pour que le recalcul depuis le brut
/// donne exactement le même texte.
/// </summary>
public sealed class LayoutTable
{
    /// <summary>Cases 0 à 7 : <see cref="ShiftState"/>. Case 8 : pavé numérique avec Verr Num.</summary>
    public const int SlotCount = 9;
    public const int NumLockSlot = 8;
    private const byte FormatVersion = 1;

    private readonly char[] _chars = new char[KeyCode.ScanCodeLimit * SlotCount];
    private readonly bool[] _dead = new bool[KeyCode.ScanCodeLimit * SlotCount];
    private Dictionary<char, (ushort Key, int Slot)>? _reverse;
    private HashSet<char> _shared = [];

    public LayoutTable(string id, bool hasAltGr)
    {
        Id = id;
        HasAltGr = hasAltGr;
    }

    /// <summary>Identifiant Windows de la disposition (KLID, par exemple « 0000040C » pour le français).</summary>
    public string Id { get; }

    /// <summary>La touche Alt de droite est AltGr (Ctrl+Alt) sur cette disposition.</summary>
    public bool HasAltGr { get; }

    /// <summary>Disposition AZERTY française de Windows, intégrée : repli et tests.</summary>
    public static LayoutTable FrenchAzerty { get; } = FrenchAzertyLayout.Create();

    public void Set(ushort key, int slot, char c, bool isDead = false)
    {
        int i = Index(key, slot);
        if (i < 0) throw new ArgumentOutOfRangeException(nameof(key));
        _chars[i] = c;
        _dead[i] = isDead && c != '\0';
        _reverse = null;
    }

    /// <summary>Caractère produit, ou '\0'. <paramref name="isDead"/> : touche morte (accent en attente).</summary>
    public char Get(ushort key, int slot, out bool isDead)
    {
        int i = Index(key, slot);
        if (i < 0) { isDead = false; return '\0'; }
        isDead = _dead[i];
        return _chars[i];
    }

    /// <summary>Touche et case qui produisent ce caractère directement (sans touche morte).</summary>
    public bool TryFindKey(char c, out ushort key, out int slot)
    {
        _reverse ??= BuildReverse();
        if (_reverse.TryGetValue(c, out var hit)) { key = hit.Key; slot = hit.Slot; return true; }
        key = 0; slot = 0;
        return false;
    }

    /// <summary>
    /// Vrai si une seule touche produit ce caractère directement : faux pour les chiffres et les opérateurs que le pavé
    /// numérique double, dont la touche ne se déduit pas du caractère.
    /// </summary>
    public bool HasSingleKey(char c)
    {
        _reverse ??= BuildReverse();
        return _reverse.ContainsKey(c) && !_shared.Contains(c);
    }

    private Dictionary<char, (ushort, int)> BuildReverse()
    {
        var map = new Dictionary<char, (ushort, int)>();
        var shared = new HashSet<char>();
        // On parcourt les cases dans l'ordre (sans modificateur d'abord) : la combinaison la plus simple gagne.
        // Les cases avec Verr Maj sont ignorées : elles doublent les autres.
        for (int slot = 0; slot < SlotCount; slot++)
        {
            if (slot != NumLockSlot && (slot & (int)ShiftState.Caps) != 0) continue;
            for (ushort key = 1; key < KeyCode.ScanCodeLimit; key++)
            {
                int i = key * SlotCount + slot;
                if (_chars[i] == '\0' || _dead[i]) continue;
                if (!map.TryAdd(_chars[i], (key, slot)) && map[_chars[i]].Item1 != key) shared.Add(_chars[i]);
            }
        }
        _shared = shared;
        return map;
    }

    private static int Index(ushort key, int slot) =>
        key < KeyCode.ScanCodeLimit && (uint)slot < SlotCount ? key * SlotCount + slot : -1;

    public byte[] Serialize()
    {
        var w = new ByteWriter(2048);
        w.WriteByte(FormatVersion);
        w.WriteByte(HasAltGr ? (byte)1 : (byte)0);
        byte[] id = Encoding.UTF8.GetBytes(Id);
        w.WriteVarint((ulong)id.Length);
        w.Write(id);
        int count = 0;
        for (int i = 0; i < _chars.Length; i++) if (_chars[i] != '\0') count++;
        w.WriteVarint((ulong)count);
        for (int i = 0; i < _chars.Length; i++)
        {
            if (_chars[i] == '\0') continue;
            w.WriteVarint((ulong)(i / SlotCount));
            w.WriteByte((byte)(i % SlotCount | (_dead[i] ? 0x80 : 0)));
            w.WriteVarint(_chars[i]);
        }
        return w.ToArray();
    }

    public static LayoutTable Deserialize(ReadOnlySpan<byte> data)
    {
        int pos = 0;
        if (data.Length < 2 || data[pos++] != FormatVersion) throw new InvalidDataException("Version de disposition inconnue.");
        bool altGr = data[pos++] != 0;
        int idLen = Varint.ReadInt(data, ref pos);
        if (pos + idLen > data.Length) throw new InvalidDataException("Disposition tronquée.");
        string id = Encoding.UTF8.GetString(data.Slice(pos, idLen));
        pos += idLen;
        var t = new LayoutTable(id, altGr);
        int count = Varint.ReadInt(data, ref pos);
        for (int n = 0; n < count; n++)
        {
            int key = Varint.ReadInt(data, ref pos);
            if (pos >= data.Length) throw new InvalidDataException("Disposition tronquée.");
            byte slot = data[pos++];
            int c = Varint.ReadInt(data, ref pos);
            if (key >= KeyCode.ScanCodeLimit || (slot & 0x7F) >= SlotCount || c > char.MaxValue)
                throw new InvalidDataException("Entrée de disposition invalide.");
            t.Set((ushort)key, slot & 0x7F, (char)c, (slot & 0x80) != 0);
        }
        return t;
    }

    /// <summary>Empreinte du contenu (16 octets) : une disposition identique n'est stockée qu'une fois.</summary>
    public byte[] ComputeHash() => SHA256.HashData(Serialize()).AsSpan(0, 16).ToArray();
}
