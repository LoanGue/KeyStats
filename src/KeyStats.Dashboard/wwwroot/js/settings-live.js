// Carte « Compteur à l'écran » : interrupteur, coin de l'écran et opacité, enregistrés à chaque changement.

import { el } from "./states.js";
import { saveSettings, settingsCard, statusLine } from "./settings-card.js";

const cornerLabels = [
  ["topLeft", "Haut gauche"],
  ["topRight", "Haut droite"],
  ["bottomLeft", "Bas gauche"],
  ["bottomRight", "Bas droite"],
];

const messages = {
  applied: "Enregistré. L'agent applique ce réglage dès maintenant.",
  pending:
    "Enregistré. L'agent appliquera ce réglage à son prochain démarrage.",
};

function toggle() {
  const node = el("button", "settings-switch");
  node.type = "button";
  node.setAttribute("role", "switch");
  node.setAttribute("aria-label", "Afficher le compteur");
  node.append(el("span", "settings-switch-knob"));
  return node;
}

/** Groupe de boutons radio à tabulation unique : flèches, Début et Fin changent le coin choisi. */
function cornerGroup(onPick) {
  const group = el("div", "settings-corners");
  group.setAttribute("role", "radiogroup");
  group.setAttribute("aria-label", "Position du compteur");
  const buttons = cornerLabels.map(([id, label]) => {
    const b = el("button", "settings-corner");
    b.type = "button";
    b.setAttribute("role", "radio");
    b.dataset.corner = id;
    const screen = el("span", "settings-corner-screen");
    screen.setAttribute("aria-hidden", "true");
    screen.append(el("span", `settings-corner-mark ${id}`));
    b.append(screen, el("span", null, label));
    b.addEventListener("click", () => onPick(id));
    return b;
  });
  group.addEventListener("keydown", (e) => {
    const i = buttons.indexOf(document.activeElement);
    const last = buttons.length - 1;
    const next = i === last ? 0 : i + 1;
    const previous = i === 0 ? last : i - 1;
    const target = {
      ArrowRight: next,
      ArrowDown: next,
      ArrowLeft: previous,
      ArrowUp: previous,
      Home: 0,
      End: last,
    }[e.key];
    if (target === undefined || i < 0) return;
    e.preventDefault();
    buttons[target].focus();
    onPick(buttons[target].dataset.corner);
  });
  group.append(...buttons);
  const select = (id) => {
    for (const b of buttons) {
      const on = b.dataset.corner === id;
      b.setAttribute("aria-checked", String(on));
      b.tabIndex = on ? 0 : -1;
    }
  };
  return { group, buttons, select };
}

function opacitySlider(onChange) {
  const part = el("div", "settings-live-part");
  const value = el("span", "settings-unit");
  value.setAttribute("aria-hidden", "true");
  const range = el("input");
  range.type = "range";
  range.min = "20";
  range.max = "100";
  range.step = "5";
  range.setAttribute("aria-label", "Opacité");
  const line = el("span", "settings-range-head");
  line.append(el("span", "settings-subtitle", "Opacité"), value);
  part.append(line, range);
  const show = (v) => {
    value.textContent = `${v} %`;
    range.setAttribute("aria-valuetext", `${v} %`);
  };
  range.addEventListener("input", () => show(range.value));
  range.addEventListener("change", () => onChange(Number(range.value)));
  const set = (v) => {
    range.value = String(v);
    show(v);
  };
  return { part, range, set };
}

export function liveCard(settings) {
  const { section, head } = settingsCard(
    "Compteur à l'écran",
    "Une petite fenêtre qui affiche votre vitesse en direct.",
  );
  // live : l'état enregistré ; shown : ce que la carte affiche, changements en attente compris.
  let live = settings.liveCounter;
  let shown = live;
  const status = statusLine();
  const sw = toggle();
  head.append(sw);

  const body = el("div", "settings-live");
  const picker = cornerGroup((id) => change({ position: id }));
  const position = el("div", "settings-live-part");
  position.append(el("span", "settings-subtitle", "Position"), picker.group);
  const opacity = opacitySlider((v) => change({ opacity: v }));
  body.append(position, opacity.part);

  function draw() {
    sw.setAttribute("aria-checked", String(shown.enabled));
    picker.select(shown.position);
    opacity.set(shown.opacity);
    body.classList.toggle("off", !shown.enabled);
    for (const c of [opacity.range, ...picker.buttons])
      c.disabled = !shown.enabled;
  }

  // Les contrôles restent actifs pendant l'enregistrement, pour garder le focus clavier : un changement
  // fait entre-temps est fusionné aux autres et envoyé ensuite, jamais perdu.
  let queued = null;
  let busy = false;
  async function change(patch) {
    shown = { ...shown, ...patch };
    queued = { ...queued, ...patch };
    draw();
    if (busy) return;
    busy = true;
    while (queued && section.isConnected) {
      const sent = queued;
      queued = null;
      const result = await saveSettings(
        { liveCounter: sent },
        status,
        messages,
      );
      if (result) live = result.settings.liveCounter;
    }
    busy = false;
    queued = null;
    shown = live;
    draw();
  }

  sw.addEventListener("click", () => change({ enabled: !shown.enabled }));
  section.append(body, status.node);
  draw();
  return section;
}
