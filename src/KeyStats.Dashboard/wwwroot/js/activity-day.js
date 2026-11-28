// Carte « Une journée, minute par minute » de la page Activité (/api/activity/day, maquette Activite).

import { levels } from "./activity-ramp.js";
import { getJson } from "./api.js";
import { describedBy, fillTable } from "./chart-card.js";
import { duration, isoDayLong, nf, shortDelay } from "./format.js";
import { el } from "./states.js";

const svgNs = "http://www.w3.org/2000/svg";
const width = 960;
const bar = 40;
const arrows = { prev: "M15 6l-6 6 6 6", next: "M9 6l6 6-6 6" };

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
      (s) => t + 30000 >= s.startMs && t + 30000 < s.endMs,
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

/** Une ligne par heure locale écoulée : touches et temps non observé ; une heure répétée est signalée. */
function hourRows(day) {
  const n = day.keys.length;
  const seen = new Set();
  return day.hours.flatMap((h, k) => {
    if (day.startMs + h.minute * 60000 >= day.observedToMs) return [];
    const again = seen.has(h.hour);
    seen.add(h.hour);
    const end = day.hours[k + 1]?.minute ?? n;
    const keys = day.keys.slice(h.minute, end).reduce((a, b) => a + b, 0);
    const from = day.startMs + h.minute * 60000;
    const to = day.startMs + end * 60000;
    const blind = day.unobserved.reduce(
      (sum, s) =>
        sum + Math.max(0, Math.min(to, s.endMs) - Math.max(from, s.startMs)),
      0,
    );
    const name = `${h.hour}\u00a0h${again ? " (heure répétée)" : ""}`;
    return [[name, nf.format(keys), blind ? duration(blind) : "aucun"]];
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
    `Frise du ${isoDayLong(day.day)} : frappe, pauses et temps non observé`,
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

const title = "Une journée, minute par minute";
const unread = "La journée n'a pas pu être lue.";

/** Carte de la frise ; day : réponse de /api/activity/day, ou null si elle a échoué ; gapMs : seuil de pause, ou null. */
export function dayCard(day, gapMs, ctx) {
  if (!day) return { title, empty: unread, wide: true };
  const body = el("div", "day-body");
  const label = el("span", "day-label");
  label.setAttribute("aria-live", "polite");
  const prev = stepButton("prev", "Jour précédent");
  const next = stepButton("next", "Jour suivant");
  const nav = el("div", "day-nav");
  nav.append(prev, label, next);
  let shown = day;
  let loading = false;
  const show = (d) => {
    shown = d;
    label.textContent = isoDayLong(d.day);
    // Un bouton désactivé perd le focus : il passe à l'autre bouton, pour enchaîner les jours au clavier.
    const focused = document.activeElement;
    prev.disabled = !d.prev;
    next.disabled = !d.next;
    if (focused?.disabled) (focused === prev ? next : prev).focus();
    body.replaceChildren(...frieze(d), legend(d, gapMs));
  };
  // Pendant la lecture, les boutons restent actifs (et gardent le focus) mais ne font rien.
  const go = async (target) => {
    if (loading) return;
    loading = true;
    nav.setAttribute("aria-busy", "true");
    try {
      const d = await getJson(`/api/activity/day?day=${target}`);
      if (ctx.isCurrent() && body.isConnected) show(d);
    } catch {
      if (!ctx.isCurrent() || !body.isConnected) return;
      show(shown);
      const note = el("p", "activity-note", unread);
      note.setAttribute("role", "status");
      body.prepend(note);
    } finally {
      loading = false;
      nav.removeAttribute("aria-busy");
    }
  };
  prev.addEventListener("click", () => go(shown.prev));
  next.addEventListener("click", () => go(shown.next));
  show(day);
  return {
    title,
    note: "Plus la minute est foncée, plus vous y avez tapé.",
    aside: nav,
    wide: true,
    body,
  };
}
