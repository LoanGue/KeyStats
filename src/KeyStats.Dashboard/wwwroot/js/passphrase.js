// Règles de la phrase de secours partagées par l'assistant et la carte Chiffrement, alignées sur
// KeyStats.Core pour que la page refuse ce que le tableau de bord refuserait.

/** Longueur minimale, comme KeySetup.MinPassphraseLength dans KeyStats.Core. */
export const MinLength = 12;

/** Même forme que KeyWrap.NormalizePassphrase : NFC, bords retirés, suites d'espaces réduites. */
export const normalized = (s) =>
  s.normalize("NFC").trim().split(/\s+/).join(" ");
