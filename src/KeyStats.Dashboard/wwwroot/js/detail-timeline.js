// Frise interactive de la page Détail (maquette Détail) : zoom, position de la fenêtre, touche choisie
// et ses mesures, tableau pour les lecteurs d'écran. Le dessin vit dans detail-frieze.js.

import { describedBy, fillTable } from "./chart-card.js";
import {
  drawFrieze,
  end,
  friezeSvg,
  keyFull,
  keyLabel,
  offset,
} from "./detail-frieze.js";
import { shortDelay } from "./format.js";
import { el } from "./states.js";

/** Fenêtres proposées par le zoom, en millisecondes. */
const spans = [
  250, 500, 1000, 2000, 5000, 10e3, 30e3, 60e3, 120e3, 300e3, 600e3, 1800e3,
];
const firstSpan = 1000;
/** Crans du curseur de position par largeur de fenêtre. */
const notches = 20;
const columns = [
  "Touche",
  "Enfoncée à",
  "Durée d'appui",
  "Depuis la touche précédente",
  "Chevauchement",
  "Effacée ensuite",
];
const legendItems = [
  ["Appui, de l'enfoncement au relâchement", "press"],
  ["Touche effacée ensuite", "erased"],
  ["Retour arrière", "backspace"],
  ["Chevauchement", "overlap"],
];

function zoomButton(label, text) {
  const button = el("button", "detail-zoom", text);
  button.type = "button";
  if (label) button.setAttribute("aria-label", label);
  return button;
}

function legend() {
  const box = el("div", "detail-legend");
  for (const [label, cls] of legendItems) {
    const item = el("span");
    const mark = el("span", `detail-swatch ${cls}`);
    mark.setAttribute("aria-hidden", "true");
    item.append(mark, label);
    box.append(item);
  }
  return box;
}

/** Mesures de l'appui choisi ; la frise, en curseur, les annonce elle-même. */
function factsRow(presses) {
  const box = el("div", "detail-facts");
  box.setAttribute("aria-hidden", "true");
  const kbd = el("kbd", "detail-kbd");
  const values = columns.slice(1, 5).map((label) => {
    const cell = el("div");
    const value = el("div", "detail-fact-value");
    cell.append(el("div", "detail-fact-label", label), value);
    box.append(cell);
    return value;
  });
  box.prepend(kbd);
  const show = (i) => {
    const p = presses[i];
    kbd.textContent = keyLabel(p.key);
    row(presses, i)
      .slice(1, 5)
      .forEach((text, k) => (values[k].textContent = text));
  };
  return { box, show };
}

/** Ligne du tableau pour l'appui i, dans l'ordre de columns. */
function row(presses, i) {
  const p = presses[i];
  return [
    keyFull(p.key),
    offset(p.downMs),
    p.holdMs == null ? "inconnue" : shortDelay(p.holdMs),
    i ? shortDelay(p.downMs - presses[i - 1].downMs) : "première touche",
    p.overlapMs ? shortDelay(p.overlapMs) : "aucun",
    p.erased ? "oui" : "non",
  ];
}

/**
 * Frise d'une session : presses de /api/detail/keys, au moins un appui. Renvoie aside (zoom), note
 * (fenêtre affichée) et body ; title est donné par l'appelant. La frise est un curseur : la valeur
 * est l'appui choisi, que les flèches déplacent.
 */
export function timeline(presses, durationMs, sessionText) {
  const total = Math.max(1, durationMs, ...presses.slice(-50).map(end));
  let span = Math.min(total, firstSpan);
  let from = 0;
  let sel = 0;

  const note = el("span");
  const zoomOut = zoomButton("Dézoomer", "−");
  const zoomIn = zoomButton("Zoomer", "+");
  const whole = zoomButton(null, "Toute la session");
  const aside = el("div", "detail-zoom-group");
  aside.setAttribute("role", "group");
  aside.setAttribute("aria-label", "Zoom");
  aside.append(zoomOut, zoomIn, whole);

  const chart = friezeSvg();
  const { hidden, table } = describedBy(chart, "slider");
  // Nom fixe : la fenêtre affichée est dite par la note et le curseur de position.
  chart.setAttribute(
    "aria-label",
    "Frise des appuis de la session ; valeurs de la fenêtre dans le tableau qui suit. Flèches gauche et droite : touche précédente ou suivante.",
  );
  chart.setAttribute("aria-valuemin", "1");
  chart.setAttribute("aria-valuemax", String(presses.length));
  const position = el("input", "detail-position");
  position.type = "range";
  position.min = "0";
  position.setAttribute("aria-label", "Position de la fenêtre dans la session");
  const facts = factsRow(presses);
  const body = el("div", "detail-body");
  body.append(chart, hidden, position, legend(), facts.box);

  const clamp = (f) => Math.max(0, Math.min(f, total - span));
  /** Un appui est visible quand il tient entier dans la fenêtre. */
  const visible = (p) => p.downMs >= from && end(p) <= from + span;
  /** Pas du curseur de position : la fenêtre en notches crans, le dernier cran touche la fin. */
  const notch = () => span / notches;

  function draw() {
    const to = from + span;
    const range = `${offset(from)} – ${offset(to)}`;
    const shown = drawFrieze(chart, presses, { from, span, sel });
    const p = presses[sel];
    chart.setAttribute("aria-valuenow", String(sel + 1));
    chart.setAttribute(
      "aria-valuetext",
      `${keyFull(p.key)}, enfoncée à ${offset(p.downMs)}, durée d'appui ${row(presses, sel)[2]}`,
    );
    fillTable(
      table,
      columns,
      shown.map((i) => row(presses, i)),
    );
    note.textContent = `${sessionText} · fenêtre affichée : ${range}`;
    position.hidden = span >= total;
    position.max = String(Math.ceil((total - span) / notch()));
    position.value = String(Math.round(from / notch()));
    position.setAttribute("aria-valuetext", range);
    zoomIn.disabled = span <= spans[0];
    zoomOut.disabled = whole.disabled = span >= total;
    facts.show(sel);
  }

  /** Choisit l'appui i et déplace la fenêtre s'il en sort. */
  function select(i) {
    sel = Math.max(0, Math.min(i, presses.length - 1));
    const p = presses[sel];
    if (!visible(p)) from = clamp(p.downMs - span / 5);
    draw();
  }

  /** Nouvelle largeur de fenêtre, centrée sur l'appui choisi s'il est visible. */
  function zoom(next) {
    const p = presses[sel];
    const center = visible(p) ? p.downMs : from + span / 2;
    span = Math.min(total, next);
    from = clamp(center - span / 2);
    draw();
  }

  zoomIn.addEventListener("click", () =>
    zoom(spans.findLast((s) => s < span) ?? spans[0]),
  );
  zoomOut.addEventListener("click", () =>
    zoom(spans.find((s) => s > span) ?? total),
  );
  whole.addEventListener("click", () => zoom(total));
  position.addEventListener("input", () => {
    from = clamp(Number(position.value) * notch());
    draw();
  });
  chart.addEventListener("click", (event) => {
    const hit = event.target.closest("[data-index]");
    if (hit) select(Number(hit.dataset.index));
  });
  chart.addEventListener("keydown", (event) => {
    const moves = {
      ArrowLeft: sel - 1,
      ArrowRight: sel + 1,
      Home: 0,
      End: presses.length - 1,
    };
    if (!(event.key in moves)) return;
    event.preventDefault();
    select(moves[event.key]);
  });

  draw();
  return { aside, note, body };
}
