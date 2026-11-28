// Carte « Une journée, minute par minute » de la page Activité (/api/activity/day, maquette Activite).

import { levels } from "./activity-ramp.js";
import { getJson } from "./api.js";
import { describedBy, fillTable } from "./chart-card.js";
import { duration, nf, shortDelay } from "./format.js";
import { el } from "./states.js";

const svgNs = "http://www.w3.org/2000/svg";
const width = 960;
const bar = 40;
const arrows = { prev: "M15 6l-6 6 6 6", next: "M9 6l6 6-6 6" };
// Jour aaaa-mm-jj lu en minuit UTC : le fuseau local décalerait la date.
const dayFull = new Intl.DateTimeFormat("fr-FR", {
  weekday: "long",
  day: "numeric",
  month: "long",
  year: "numeric",
  timeZone: "UTC",
});

function svg(tag, attrs) {
  const node = document.createElementNS(svgNs, tag);
  for (const [k, v] of Object.entries(attrs)) node.setAttribute(k, v);
  return node;
}

/** Nature de chaque minute : niveau 1 à 4 de frappe, pause, non observé, ou null pour le reste à venir. */
function minuteKinds(day) {
  const level = levels(day.keys);
  return day.keys.map((keys, i) => {
    const t = day.startMs + i * 60000;
    if (t >= day.observedToMs) return null;
    if (keys > 0) return `level-${level(keys)}`;
    const blind = day.unobserved.some(
      (s) => t + 30000 > s.startMs && t + 30000 <= s.endMs,
    );
    return blind ? "unobserved" : "pause";
  });
}

/** Un rectangle par suite de minutes de même nature, regroupés en un chemin par nature. */
function bands(day) {
  const kinds = minuteKinds(day);
  const x = (i) => ((i * width) / kinds.length).toFixed(2);
  const paths = {};
  for (let i = 0; i < kinds.length;) {
    const kind = kinds[i];
    let j = i + 1;
    while (j < kinds.length && kinds[j] === kind) j++;
    if (kind)
      paths[kind] = `${paths[kind] ?? ""}M${x(i)} 0H${x(j)}V${bar}H${x(i)}Z`;
    i = j;
  }
  return Object.entries(paths).map(([kind, d]) =>
    svg("path", { class: `day-${kind}`, d }),
  );
}

/** Graduations toutes les trois heures, prises sur les heures locales du jour (changements d'heure compris). */
function tickMarks(day) {
  const n = day.keys.length;
  const seen = new Set();
  const marks = day.hours.filter(
    (h) => h.hour % 3 === 0 && !seen.has(h.hour) && seen.add(h.hour),
  );
  return [...marks.map((h) => [(h.minute * width) / n, h.hour]), [width, 24]];
}

function tickLines(marks) {
  let d = "";
  for (const [x] of marks) {
    const edge = x === 0 || x === width;
    d += `M${x.toFixed(2)} ${edge ? 0 : bar + 4}V${bar + 10}`;
  }
  return svg("path", { class: "day-tick", d });
}

/** Libellés des heures en HTML, placés en pourcentage : ils gardent leur taille quand la frise rétrécit. */
function tickLabels(marks) {
  const axis = el("div", "day-axis");
  axis.setAttribute("aria-hidden", "true");
  for (const [x, hour] of marks) {
    const label = el("span", null, `${hour}\u00a0h`);
    label.style.left = `${(x / width) * 100}%`;
    if (x === 0) label.className = "day-axis-start";
    if (x === width) label.className = "day-axis-end";
    axis.append(label);
  }
  return axis;
}

/** Une ligne par heure locale : touches et temps non observé. */
function hourRows(day) {
  const n = day.keys.length;
  return day.hours.map((h, k) => {
    const end = day.hours[k + 1]?.minute ?? n;
    const keys = day.keys.slice(h.minute, end).reduce((a, b) => a + b, 0);
    const from = day.startMs + h.minute * 60000;
    const to = day.startMs + end * 60000;
    const blind = day.unobserved.reduce(
      (sum, s) =>
        sum + Math.max(0, Math.min(to, s.endMs) - Math.max(from, s.startMs)),
      0,
    );
    return [
      `${h.hour}\u00a0h`,
      nf.format(keys),
      blind ? duration(blind) : "aucun",
    ];
  });
}

function frieze(day) {
  const chart = svg("svg", {
    viewBox: `0 0 ${width} ${bar + 10}`,
    width: "100%",
    class: "day-frieze",
  });
  const hatch = svg("pattern", {
    id: "day-hatch",
    width: 6,
    height: 6,
    patternUnits: "userSpaceOnUse",
    patternTransform: "rotate(45)",
  });
  hatch.append(
    svg("rect", { class: "day-hatch-bg", width: 6, height: 6 }),
    svg("path", { class: "day-hatch", d: "M0 0V6" }),
  );
  const defs = svg("defs", {});
  defs.append(hatch);
  const marks = tickMarks(day);
  chart.append(defs, ...bands(day), tickLines(marks));
  chart.setAttribute(
    "aria-label",
    `Frise du ${dayFull.format(new Date(`${day.day}T00:00:00Z`))} : frappe, pauses et temps non observé`,
  );
  const { hidden, table } = describedBy(chart);
  fillTable(table, ["Heure", "Touches", "Non observé"], hourRows(day));
  return [chart, tickLabels(marks), hidden];
}

function legendItem(kind, label, value, detail) {
  const item = el("span", "day-legend-item");
  const swatch = el("span", `day-swatch day-swatch-${kind}`);
  swatch.setAttribute("aria-hidden", "true");
  item.append(
    swatch,
    label,
    " ",
    el("span", "day-legend-value", detail ? `${value} · ${detail}` : value),
  );
  return item;
}

function legend(day, gapMs) {
  const box = el("div", "day-legend");
  box.append(
    legendItem("typing", "Frappe", duration(day.writingMs)),
    legendItem(
      "pause",
      "Pause",
      duration(day.pauseMs),
      gapMs ? `au-delà de ${shortDelay(gapMs)} sans touche` : null,
    ),
    legendItem(
      "unobserved",
      "Non observé",
      duration(day.unobservedMs),
      "agent arrêté ou session Windows fermée",
    ),
  );
  return box;
}

function stepButton(dir, label) {
  const button = el("button", "icon-btn activity-step");
  button.type = "button";
  button.setAttribute("aria-label", label);
  button.innerHTML = `<svg viewBox="0 0 24 24" aria-hidden="true"><path d="${arrows[dir]}"/></svg>`;
  return button;
}

/** Carte de la frise ; day : réponse de /api/activity/day, gapMs : seuil de pause des réglages, ou null. */
export function dayCard(day, gapMs, ctx) {
  const body = el("div", "day-body");
  const label = el("span", "day-label");
  label.setAttribute("aria-live", "polite");
  const prev = stepButton("prev", "Jour précédent");
  const next = stepButton("next", "Jour suivant");
  const nav = el("div", "day-nav");
  nav.append(prev, label, next);
  let shown = day;
  const show = (d) => {
    shown = d;
    label.textContent = dayFull.format(new Date(`${d.day}T00:00:00Z`));
    prev.disabled = !d.prev;
    next.disabled = !d.next;
    body.replaceChildren(...frieze(d), legend(d, gapMs));
  };
  const go = async (target) => {
    prev.disabled = next.disabled = true;
    try {
      const d = await getJson(`/api/activity/day?day=${target}`);
      if (ctx.isCurrent() && body.isConnected) show(d);
    } catch {
      if (!ctx.isCurrent() || !body.isConnected) return;
      show(shown);
      body.prepend(el("p", "activity-note", "La journée n'a pas pu être lue."));
    }
  };
  prev.addEventListener("click", () => go(shown.prev));
  next.addEventListener("click", () => go(shown.next));
  show(day);
  return {
    title: "Une journée, minute par minute",
    note: "Plus la minute est foncée, plus vous y avez tapé.",
    aside: nav,
    wide: true,
    body,
  };
}
