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

/** Marque la touche choisie dans root, moitié basse d'Entrée comprise. */
function markSelected(root, code) {
  for (const node of root.querySelectorAll(".key[data-code]")) {
    const on = Number(node.dataset.code) === code;
    node.classList.toggle("selected", on);
    if (node.tagName === "BUTTON")
      node.setAttribute("aria-pressed", String(on));
  }
}

/** Touche choisie par défaut : la plus utilisée parmi celles de la carte, ou null. */
const busiest = (data) =>
  data.keys.reduce(
    (best, k) =>
      keyOfCode(k.code) && k.presses > (best?.presses ?? 0) ? k : best,
    null,
  )?.code ?? null;

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
  const name = keyName(k);
  const footnote = data.dayTablesUnfiltered ? unfiltered : undefined;
  let spec;
  try {
    const minutes = await getJson(`/api/keyboard/minutes${query}`);
    spec = detailCard(k, name, data, minutes, footnote);
  } catch {
    spec = {
      title: `Touche ${name}`,
      empty: "Le détail de cette touche n'a pas pu être lu.",
    };
  }
  if (!ctx.isCurrent() || serial !== state.serial) return;
  replaceDetail(state, spec);
  state.live.textContent = `Détail de la touche ${name} affiché.`;
}

/** Remplace la carte de détail par celle de spec et trace son graphique. */
function replaceDetail(state, spec) {
  const card = chartCard({ ...spec, className: "key-detail-card" });
  state.detail.replaceWith(card.section);
  state.detail = card.section;
  drawCharts([card], state.isCurrent);
}

export async function renderKeyboard(view, ctx) {
  const raw = await getJson(`/api/keyboard${ctx.query}`);
  if (!ctx.isCurrent()) return;
  if (!raw.presses) {
    showEmptyPeriod(view, ctx);
    return;
  }
  const data = prepare(raw);
  const state = {
    selected: busiest(data),
    serial: 0,
    isCurrent: ctx.isCurrent,
    live: el("p", "sr-only"),
  };
  state.live.setAttribute("aria-live", "polite");
  const k = state.selected == null ? null : keyOfCode(state.selected);
  state.detail = chartCard({
    title: k ? `Touche ${keyName(k)}` : "Détail d'une touche",
    empty: k ? "Lecture du détail…" : "Cliquez une touche pour son détail.",
    className: "key-detail-card",
  }).section;
  const grid = el("div", "chart-grid");
  const pick = (code) => {
    state.selected = code;
    markSelected(grid, code);
    showDetail(state, data, ctx, code);
  };
  grid.append(heatCard(data, state, pick), state.detail);
  view.replaceChildren(grid, state.live);
  if (state.selected != null)
    await showDetail(state, data, ctx, state.selected);
}
