// Briques communes aux étapes de l'assistant de première installation : icônes, notes, boutons,
// messages de champ et en-tête d'étape.

import { el } from "./states.js";

/** Étapes de l'assistant, dans l'ordre. */
export const steps = [
  "Bienvenue",
  "Phrase de secours",
  "Windows Hello",
  "Confirmation",
];

const icons = {
  lock: '<rect x="5" y="11" width="14" height="10" rx="2"/><path d="M8 11V8a4 4 0 0 1 8 0v3"/>',
  chart: '<path d="M4 19V10M10 19V5M16 19v-7M22 19H2"/>',
  info: '<circle cx="12" cy="12" r="9"/><path d="M12 11v5M12 7.5h0"/>',
  error: '<circle cx="12" cy="12" r="9"/><path d="M12 7.5v5M12 16.5h0"/>',
  warn: '<path d="M12 3l9.5 17h-19z"/><path d="M12 10v4M12 17.5h0"/>',
  check: '<path d="M5 12.5l4.5 4.5L19 7.5"/>',
  dash: '<path d="M6 12h12"/>',
  arrow: '<path d="M5 12h14M13 6l6 6-6 6"/>',
  eye: '<path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12z"/><circle cx="12" cy="12" r="3"/>',
};

/** Icône décorative ; les tracés viennent de la table ci-dessus, jamais d'une donnée. */
export function icon(name) {
  const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  svg.setAttribute("viewBox", "0 0 24 24");
  svg.setAttribute("aria-hidden", "true");
  svg.innerHTML = icons[name];
  return svg;
}

export function note(kind, name, text) {
  const node = el("div", `setup-note setup-note-${kind}`);
  node.setAttribute("role", "note");
  node.append(icon(name), el("span", null, text));
  return node;
}

export function action(label, primary, onClick, arrow) {
  const node = el(
    "button",
    primary ? "btn btn-primary setup-btn" : "btn setup-btn",
    label,
  );
  node.type = "button";
  if (arrow) node.append(icon("arrow"));
  node.addEventListener("click", onClick);
  return node;
}

/** Message sous un champ : erreur ou confirmation, masqué tant qu'il n'a rien à dire. */
export function fieldMessage(id, kind, name, text) {
  const node = el("span", `setup-msg setup-msg-${kind}`);
  node.id = id;
  node.hidden = true;
  node.append(icon(name), text);
  return node;
}

/** En-tête d'une étape : numéro, titre focalisable et texte. */
export function intro(n, title, text) {
  const head = el("div");
  const h1 = el("h1", null, title);
  h1.tabIndex = -1;
  head.append(
    el("span", "setup-step", `Étape ${n} sur ${steps.length}`),
    h1,
    el("p", null, text),
  );
  return { head, h1 };
}
