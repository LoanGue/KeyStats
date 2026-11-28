// Dessin de la frise touche par touche (maquette Détail) : deux voies d'appuis, la rangée des
// chevauchements et l'axe du temps, pour une fenêtre de la session. Chaque appui porte le libellé de
// sa case AZERTY ; le texte produit n'est pas reconstitué.

import { nf } from "./format.js";
import { keyName, keyOfCode } from "./keyboard-layout.js";

const NS = "http://www.w3.org/2000/svg";
const width = 640;
const height = 172;
const left = 48;
const plot = width - left;
const lanes = [18, 58];
const laneHeight = 24;
const overlapY = 100;
const axisY = 150;
const steps = [
  10, 20, 50, 100, 200, 500, 1e3, 2e3, 5e3, 10e3, 20e3, 30e3, 60e3, 120e3,
  300e3,
];
/** Appui dont le relâchement n'a pas été vu : tracé sur cette durée, en pointillés. */
const unseenHold = 100;
const glyphs = { 0x0e: "⌫", 0x0f: "⇥", 0x1c: "↵", 0x11c: "↵", 0x39: "␣" };
const upTo1 = new Intl.NumberFormat("fr-FR", { maximumFractionDigits: 1 });

/** Libellé court d'une touche, tel que sur sa case. */
export const keyLabel = (code) => glyphs[code] ?? keyOfCode(code)?.shown ?? "?";

/** Nom complet d'une touche, pour les lecteurs d'écran. */
export function keyFull(code) {
  const k = keyOfCode(code);
  return k ? keyName(k) : `Touche ${nf.format(code)}`;
}

/** Instant depuis le début de la session : « 412 ms », « 12,4 s », « 3 min 05 s ». */
export function offset(ms) {
  // Les seuils portent sur la valeur arrondie : 999,6 ms donne « 1 s », pas « 1 000 ms ».
  if (Math.round(ms) < 1000) return `${nf.format(Math.round(ms))} ms`;
  if (Math.round(ms / 100) < 600) return `${upTo1.format(ms / 1000)} s`;
  const s = Math.round(ms / 1000);
  return `${nf.format(Math.floor(s / 60))} min ${String(s % 60).padStart(2, "0")} s`;
}

/** Fin tracée d'un appui, relâchement inconnu compris. */
export const end = (p) => p.downMs + (p.holdMs ?? unseenHold);

/** Écart d'une graduation à la première : « 200 », « 2 s », « 5 min ». */
function step(ms) {
  if (ms < 1000) return nf.format(ms);
  if (ms < 60e3) return `${upTo1.format(ms / 1000)} s`;
  return `${upTo1.format(ms / 60e3)} min`;
}

function svg(tag, attrs, text) {
  const node = document.createElementNS(NS, tag);
  for (const [name, value] of Object.entries(attrs))
    node.setAttribute(name, value);
  if (text !== undefined) node.textContent = text;
  return node;
}

/** Élément svg vide de la frise, focalisable ; drawFrieze le remplit. */
export function friezeSvg() {
  return svg("svg", {
    viewBox: `0 0 ${width} ${height}`,
    width: "100%",
    class: "detail-frieze",
    tabindex: "0",
  });
}

function hatch() {
  const defs = svg("defs", {});
  const pattern = svg("pattern", {
    id: "detail-hatch",
    width: 5,
    height: 5,
    patternUnits: "userSpaceOnUse",
    patternTransform: "rotate(45)",
  });
  pattern.append(
    svg("rect", { width: 5, height: 5, class: "detail-hatch-bg" }),
    svg("path", { d: "M0 0V5", class: "detail-hatch-line" }),
  );
  defs.append(pattern);
  return defs;
}

function laneNames() {
  const names = svg("g", { class: "detail-lane-name" });
  names.append(
    svg("text", { x: 0, y: lanes[0] + 16 }, "voie 1"),
    svg("text", { x: 0, y: lanes[1] + 16 }, "voie 2"),
    svg("text", { x: 0, y: overlapY + 10 }, "chev."),
  );
  return names;
}

/**
 * Remplit chart avec les appuis de presses visibles dans [from, from + span[ ; sel est l'indice
 * de l'appui choisi. Renvoie les indices tracés, dans l'ordre.
 */
export function drawFrieze(chart, presses, { from, span, sel }) {
  const to = from + span;
  const x = (ms) => left + ((ms - from) / span) * plot;
  /** Rectangle de start à stop, rogné aux bords de la zone tracée. */
  const box = (start, stop, y, h, cls) => {
    const x0 = Math.max(left, x(start));
    const w = Math.max(1, Math.min(width, x(stop)) - x0);
    return svg("rect", { x: x0, y, width: w, height: h, class: cls });
  };

  function bar(p, i) {
    const y = lanes[i % 2];
    const kind = p.key === 0x0e ? "backspace" : p.erased ? "erased" : "press";
    const cls = [
      "detail-bar",
      kind,
      i === sel && "selected",
      p.holdMs == null && "unseen",
    ];
    const rect = box(
      p.downMs,
      end(p),
      y,
      laneHeight,
      cls.filter(Boolean).join(" "),
    );
    rect.setAttribute("rx", 4);
    const group = svg("g", { "data-index": i });
    group.append(rect);
    const label = keyLabel(p.key);
    // Libellé posé seulement s'il tient dans la barre (environ 8 px par caractère).
    const room = 10 + 8 * label.length;
    if (Number(rect.getAttribute("width")) >= room && x(p.downMs) >= left)
      group.append(
        svg(
          "text",
          {
            x: Number(rect.getAttribute("x")) + 7,
            y: y + 17,
            class: "detail-bar-label",
          },
          label,
        ),
      );
    return group;
  }

  function axis() {
    const every = steps.find((s) => span / s <= 7) ?? steps.at(-1);
    const first = Math.ceil(from / every) * every;
    const group = svg("g", { class: "detail-axis" });
    group.append(svg("path", { d: `M${left} ${axisY}H${width}` }));
    for (let t = first; t <= to; t += every) {
      const tx = x(t);
      group.append(svg("path", { d: `M${tx} ${axisY}V${axisY + 6}` }));
      if (tx <= width - 16)
        group.append(
          svg(
            "text",
            { x: tx, y: axisY + 20 },
            t === first ? offset(t) : `+${step(t - first)}`,
          ),
        );
    }
    return group;
  }

  const shown = [];
  presses.forEach((p, i) => {
    if (p.downMs < to && end(p) > from) shown.push(i);
  });
  const overlaps = shown
    .filter((i) => presses[i].overlapMs)
    .map((i) => {
      const p = presses[i];
      return box(
        p.downMs,
        p.downMs + p.overlapMs,
        overlapY,
        14,
        "detail-overlap",
      );
    });
  chart.replaceChildren(
    hatch(),
    laneNames(),
    svg("path", { d: `M${left} 0V${axisY}`, class: "detail-origin" }),
    ...shown.map((i) => bar(presses[i], i)),
    ...overlaps,
    axis(),
  );
  return shown;
}
