using KeyStats.Core.Analysis;
using KeyStats.Core.Crypto;

namespace KeyStats.Core.Storage;

/// <summary>Une protection de la clé privée (ligne de <c>key_store</c>).</summary>
public sealed record KeyStoreEntry(
    long Id, KeyStoreKind Kind, long CreatedMs, byte[] Salt, int Iterations, byte[] Nonce, byte[] Wrapped, string? CredentialId);

/// <summary>Un titre de fenêtre chiffré, tel qu'il est stocké.</summary>
public sealed record StoredTitle(long Id, int AppId, int EpochId, byte[] Nonce, byte[] Encrypted);

/// <summary>Un bloc brut chiffré, tel qu'il est stocké.</summary>
public sealed record StoredRawBlock(long Id, long StartMs, long EndMs, long? FocusId, int EventCount, int EpochId, byte[] Nonce, byte[] Payload);

/// <summary>Une ligne de la chronologie des fenêtres.</summary>
public sealed record StoredFocus(long Id, long Ms, int AppId, long? TitleId, int? LayoutId, FocusState Flags);
