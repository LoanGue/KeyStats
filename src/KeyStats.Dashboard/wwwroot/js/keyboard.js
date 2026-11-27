// Page Clavier : carte de chaleur AZERTY du portable, pavé numérique compris (/api/keyboard),
// et détail de la touche choisie.

import { getJson } from "./api.js";
import { chartCard, drawCharts } from "./chart-card.js";
import { detailCard } from "./keyboard-detail.js";
import { heatLegend, heatmap } from "./keyboard-heatmap.js";
import { keyName, keyOfCode } from "./keyboard-layout.js";
import { measures, prepare } from "./keyboard-measures.js";
import { el, showEmptyPeriod } from "./states.js";

const unfiltered =
  "Les appuis suivent le filtre de clavier ; le filtre d'application ou de nature ne s'y applique pas, et les autres mesures couvrent toute la période.";

/** Boutons des mesures ; ils restent en place, le focus ne se perd pas au changement. */
function measurePicker(current, onPick) {
  const group = el("div", "seg small");
  group.setAttribute("role", "group");
  group.setAttribute("aria-label", "Mesure");
  const buttons = Object.entries(measures).map(([id, m]) => {
    const node = el("button", null, m.label);
    node.type = "button";
    node.dataset.measure = id;
    node.addEventListener("click", () => onPick(id));
    return node;
  });
  const press = (id) => {
    for (const b of buttons)
      b.setAttribute("aria-pressed", String(b.dataset.measure === id));
  };
  press(current);
  group.append(...buttons);
  return { group, press };
}

/** Marque la touche choisie parmi les boutons de root. */
function markSelected(root, code) {
  for (const b of root.querySelectorAll("button.key"))
    b.setAttribute("aria-pressed", String(Number(b.dataset.code) === code));
}

/** Touche choisie par défaut : la plus utilisée. */
const busiest = (data) =>
  data.keys.reduce((best, k) => (k.presses > (best?.presses ?? 0) ? k : best))
    .code;

/** Carte de chaleur, redessinée à chaque mesure choisie ; state.selected est la touche choisie. */
function heatCard(data, state, onPick) {
  let measure = "appuis";
  const note = el("span");
  note.setAttribute("aria-live", "polite");
  const body = el("div", "keyboard-body");
  const draw = () => {
    note.textContent = measures[measure].help;
    body.replaceChildren(heatmap(data, measure, onPick), heatLegend(measure));
    markSelected(body, state.selected);
    picker.press(measure);
  };
  const picker = measurePicker(measure, (id) => {
    measure = id;
    draw();
  });
  draw();
  return chartCard({
    title: "Carte de chaleur AZERTY — clavier du portable",
    note,
    aside: picker.group,
    body,
    wide: true,
    className: "keyboard-card",
    footnote: data.dayTablesUnfiltered ? unfiltered : undefined,
  }).section;
}

/** Remplace la carte de détail par celle de la touche code, une fois ses minutes lues. */
async function showDetail(state, data, ctx, code) {
  const serial = ++state.serial;
  const query = `${ctx.query ? `${ctx.query}&` : "?"}key=${code}`;
  const k = keyOfCode(code);
  let spec;
  try {
    const minutes = await getJson(`/api/keyboard/minutes${query}`);
    spec = detailCard(k, keyName(k), data, minutes);
  } catch {
    spec = {
      title: `Touche ${keyName(k)}`,
      empty: "Le détail de cette touche n'a pas pu être lu.",
    };
  }
  if (!ctx.isCurrent() || serial !== state.serial) return;
  const card = chartCard(spec);
  state.detail.replaceWith(card.section);
  state.detail = card.section;
  drawCharts([card], ctx.isCurrent);
}

export async function renderKeyboard(view, ctx) {
  const raw = await getJson(`/api/keyboard${ctx.query}`);
  if (!ctx.isCurrent()) return;
  if (!raw.presses) {
    showEmptyPeriod(view, ctx);
    return;
  }
  const data = prepare(raw);
  const state = { selected: busiest(data), serial: 0 };
  state.detail = el("section", "card chart-card");
  const grid = el("div", "chart-grid");
  const pick = (code) => {
    state.selected = code;
    markSelected(grid, code);
    showDetail(state, data, ctx, code);
  };
  grid.append(heatCard(data, state, pick), state.detail);
  view.replaceChildren(grid);
  await showDetail(state, data, ctx, state.selected);
}
