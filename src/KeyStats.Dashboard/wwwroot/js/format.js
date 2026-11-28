// Mise en forme française des nombres, durées et dates, commune aux pages.

export const nf = new Intl.NumberFormat("fr-FR");
export const nf1 = new Intl.NumberFormat("fr-FR", {
  minimumFractionDigits: 1,
  maximumFractionDigits: 1,
});

/** Part en pourcentage à une décimale : « 96,1 % ». */
export const pct = (v) => `${nf1.format(v * 100)}\u202f%`;

/** Jour court pour les graduations : « 21 nov. ». */
export const dayTick = new Intl.DateTimeFormat("fr-FR", {
  day: "numeric",
  month: "short",
});

/** Jour en toutes lettres : « vendredi 27 novembre ». */
export const dayLong = new Intl.DateTimeFormat("fr-FR", {
  weekday: "long",
  day: "numeric",
  month: "long",
});

/** Jour aaaa-mm-jj en toutes lettres, lu en minuit UTC (le fuseau local décalerait la date). */
const isoDayFull = new Intl.DateTimeFormat("fr-FR", {
  weekday: "long",
  day: "numeric",
  month: "long",
  year: "numeric",
  timeZone: "UTC",
});
export const isoDayLong = (iso) =>
  isoDayFull.format(new Date(`${iso}T00:00:00Z`));

/** Vitesse arrondie au mot près : « 58 mots/min ». */
export const wpm = (v) => `${nf.format(Math.round(v))}\u00a0mots/min`;

/** « 1 session », « 12 sessions ». */
export const sessionCount = (n) =>
  `${nf.format(n)}\u00a0session${n > 1 ? "s" : ""}`;

/** Délai court : « 412 ms », puis « 1,2 s » à partir d'une seconde. */
export const shortDelay = (v) =>
  Math.round(v) < 1000
    ? `${nf.format(Math.round(v))}\u00a0ms`
    : `${nf.format(Math.round(v / 100) / 10)}\u00a0s`;

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

/** Numéro de semaine ISO 8601 du lundi donné (aaaa-mm-jj). */
export function isoWeek(monday) {
  const d = new Date(`${monday}T12:00:00Z`);
  d.setUTCDate(d.getUTCDate() + 3); // le jeudi de la même semaine
  const jan4 = new Date(Date.UTC(d.getUTCFullYear(), 0, 4));
  return (
    1 +
    Math.round((d - jan4) / 604800000 + (((jan4.getUTCDay() + 6) % 7) - 3) / 7)
  );
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
