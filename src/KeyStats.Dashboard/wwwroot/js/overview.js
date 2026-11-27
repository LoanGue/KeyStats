// Vue d'ensemble : chiffres clés de la période et progression semaine par semaine (/api/overview).

import { getJson } from "./api.js";
import { loadCharts } from "./charts.js";
import { renderProgress } from "./progress.js";
import { el, showEmpty } from "./states.js";

const nf = new Intl.NumberFormat("fr-FR");
const nf1 = new Intl.NumberFormat("fr-FR", {
  minimumFractionDigits: 1,
  maximumFractionDigits: 1,
});

const signed = (n, text) => (n > 0 ? "+" : n < 0 ? "−" : "±") + text;

/** Durée en « 3 h 25 min », « 42 min » ou « moins d'une minute ». */
function duration(ms) {
  const minutes = Math.round(ms / 60000);
  if (minutes < 1) return "moins d'une minute";
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  if (!h) return `${m}\u00a0min`;
  return m
    ? `${nf.format(h)}\u00a0h\u00a0${m}\u00a0min`
    : `${nf.format(h)}\u00a0h`;
}

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
      // \u00c9cart des valeurs affich\u00e9es : le signe suit l'arrondi (+0,4 donne \u00b10).
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
  const [data, echarts] = await Promise.all([
    getJson(`/api/overview${ctx.query}`),
    loadCharts(),
  ]);
  if (!ctx.isCurrent()) return;
  const { current, previous, weeks } = data;
  if (!current.keys && !current.sessions) {
    const all = ctx.filters.period === "all";
    showEmpty(
      view,
      ctx.status.agentRunning,
      all ? null : () => ctx.setFilters({ ...ctx.filters, period: "all" }),
    );
    return;
  }
  const progress = renderProgress(weeks, echarts);
  view.replaceChildren(renderFigures(current, previous), progress.section);
  progress.attach();
}
