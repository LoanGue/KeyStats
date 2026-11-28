using KeyStats.Core.Crypto;
using Windows.Security.Credentials;
using Windows.Security.Cryptography;

namespace KeyStats.Dashboard;

/// <summary>
/// Création d'une clé Windows Hello liée au TPM (<c>KeyCredentialManager</c>), puis signature du défi qui
/// enveloppe la clé maîtresse. Annulation, Hello indisponible ou erreur : null, jamais d'exception.
/// </summary>
internal sealed class HelloEnroller : IHelloEnroller
{
    public async Task<byte[]?> CreateAndSignAsync(string credentialId, byte[] challenge)
    {
        try
        {
            if (!await KeyCredentialManager.IsSupportedAsync()) return null;
            using var focus = HelloSigner.BringPromptForward();
            var created = await KeyCredentialManager.RequestCreateAsync(credentialId, KeyCredentialCreationOption.ReplaceExisting);
            if (created.Status != KeyCredentialStatus.Success) return null;
            var signed = await created.Credential.RequestSignAsync(CryptographicBuffer.CreateFromByteArray(challenge));
            if (signed.Status != KeyCredentialStatus.Success) return null;
            CryptographicBuffer.CopyToByteArray(signed.Result, out byte[] signature);
            return signature;
        }
        catch (Exception)
        {
            // CsWinRT traduit les HRESULT en exceptions variées (COM, argument, annulation…) : toutes valent un refus.
            return null;
        }
    }
}
