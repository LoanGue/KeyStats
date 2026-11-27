// Vue d'ensemble : chiffres clés de la période et progression semaine par semaine (/api/overview).

import { getJson } from "./api.js";
import { duration, nf, nf1 } from "./format.js";
import { renderProgress } from "./progress.js";
import { el, showEmptyPeriod } from "./states.js";

const signed = (n, text) => (n > 0 ? "+" : n < 0 ? "−" : "±") + text;

/** Écart en pourcentage de la période précédente ; null quand elle n'a rien à comparer. */
function relative(now, before) {
  if (!before) return null;
  const pct = Math.round(((now - before) / before) * 100);
  return signed(pct, `${Math.abs(pct)}\u202f%`);
}

// Chaque indicateur : libellé, valeur affichée, précision sous la valeur, écart avec la période précédente.
const figures = [
  {
    label: "Touches",
    value: (t) => nf.format(t.keys),
    note: (t) => (t.keysInSessions ? "appuis en session" : "appuis"),
    delta: (t, p) => relative(t.keys, p.keys),
  },
  {
    label: "Mots",
    value: (t) => nf.format(t.words),
    note: () => "mots de 5 caractères",
    delta: (t, p) => relative(t.words, p.words),
  },
  {
    label: "Temps d'écriture",
    value: (t) => duration(t.writingMs),
    note: () => "pauses exclues",
    delta: (t, p) => relative(t.writingMs, p.writingMs),
  },
  {
    label: "Vitesse réelle",
    value: (t) => (t.realWpm == null ? "—" : nf.format(Math.round(t.realWpm))),
    note: () => "mots/min nets",
    delta: (t, p) => {
      if (t.realWpm == null || p.realWpm == null) return null;
      // Écart des valeurs affichées : le signe suit l'arrondi (+0,4 donne ±0).
      const gap = Math.round(t.realWpm) - Math.round(p.realWpm);
      return signed(gap, `${nf.format(Math.abs(gap))}\u00a0mots/min`);
    },
  },
  {
    label: "Précision",
    value: (t) =>
      t.accuracy == null ? "—" : `${nf1.format(t.accuracy * 100)}\u202f%`,
    note: () => "frappes justes",
    delta: (t, p) => {
      if (t.accuracy == null || p.accuracy == null) return null;
      const pts = Math.round((t.accuracy - p.accuracy) * 1000) / 10;
      return signed(pts, `${nf1.format(Math.abs(pts))}\u00a0pt`);
    },
  },
];

function renderFigures(current, previous) {
  const section = el("section", "kpis");
  section.setAttribute("aria-label", "Indicateurs");
  for (const f of figures) {
    const card = el("div", "card kpi");
    const delta = previous ? f.delta(current, previous) : null;
    const note = delta
      ? `${f.note(current)} · ${delta} vs précédente`
      : f.note(current);
    card.append(
      el("span", "kpi-label", f.label),
      el("span", "kpi-value", f.value(current)),
      el("span", "kpi-note", note),
    );
    section.append(card);
  }
  return section;
}

export async function renderOverview(view, ctx) {
  const data = await getJson(`/api/overview${ctx.query}`);
  if (!ctx.isCurrent()) return;
  const { current, previous, weeks } = data;
  if (!current.keys && !current.sessions) {
    showEmptyPeriod(view, ctx);
    return;
  }
  const progress = renderProgress(weeks);
  view.replaceChildren(renderFigures(current, previous), progress.section);
  progress.attach();
}
