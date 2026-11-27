// Mise en forme française des nombres, durées et dates, commune aux pages.

export const nf = new Intl.NumberFormat("fr-FR");
export const nf1 = new Intl.NumberFormat("fr-FR", {
  minimumFractionDigits: 1,
  maximumFractionDigits: 1,
});

/** Vitesse arrondie au mot près : « 58 mots/min ». */
export const wpm = (v) => `${nf.format(Math.round(v))}\u00a0mots/min`;

/** « 1 session », « 12 sessions ». */
export const sessionCount = (n) =>
  `${nf.format(n)}\u00a0session${n > 1 ? "s" : ""}`;

/** Durée en « 3 h 25 min », « 42 min » ou « moins d'une minute ». */
export function duration(ms) {
  const minutes = Math.round(ms / 60000);
  if (minutes < 1) return "moins d'une minute";
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  if (!h) return `${m}\u00a0min`;
  return m
    ? `${nf.format(h)}\u00a0h\u00a0${m}\u00a0min`
    : `${nf.format(h)}\u00a0h`;
}

/** Texte reçu de l'API, sûr dans une infobulle HTML. */
export const escapeHtml = (text) =>
  String(text).replace(
    /[&<>"']/g,
    (c) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[
        c
      ],
  );
