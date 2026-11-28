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
  applied: "Enregistré. Le compteur suit ce réglage dès la prochaine frappe.",
  pending:
    "Enregistré. L'agent appliquera ce réglage à son prochain démarrage.",
};

function toggle(checked) {
  const node = el("button", "settings-switch");
  node.type = "button";
  node.setAttribute("role", "switch");
  node.setAttribute("aria-label", "Afficher le compteur");
  node.setAttribute("aria-checked", String(checked));
  node.append(el("span", "settings-switch-knob"));
  return node;
}

/** Groupe de boutons radio à tabulation unique : les flèches changent le coin choisi. */
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
    const step = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 }[
      e.key
    ];
    if (!step) return;
    e.preventDefault();
    const i = buttons.indexOf(document.activeElement);
    const next = buttons[(i + step + buttons.length) % buttons.length];
    next.focus();
    onPick(next.dataset.corner);
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

export function liveCard(settings) {
  const { section, head } = settingsCard(
    "Compteur à l'écran",
    "Une petite fenêtre qui affiche votre vitesse en direct.",
  );
  let live = settings.liveCounter;
  const status = statusLine();
  const sw = toggle(live.enabled);
  head.append(sw);

  const body = el("div", "settings-live");
  const positionTitle = el("span", "settings-subtitle", "Position");
  const picker = cornerGroup((id) => {
    if (id !== live.position) save({ position: id });
  });
  const position = el("div", "settings-live-part");
  position.append(positionTitle, picker.group);

  const opacity = el("label", "settings-live-part");
  const value = el("span", "settings-unit");
  const range = el("input");
  range.type = "range";
  range.min = "20";
  range.max = "100";
  range.step = "5";
  const line = el("span", "settings-range-head");
  line.append(el("span", "settings-subtitle", "Opacité"), value);
  opacity.append(line, range);
  range.addEventListener("input", () => {
    value.textContent = `${range.value} %`;
  });
  range.addEventListener("change", () =>
    save({ opacity: Number(range.value) }),
  );
  body.append(position, opacity);

  const controls = () => [sw, range, ...picker.buttons];
  function draw() {
    sw.setAttribute("aria-checked", String(live.enabled));
    picker.select(live.position);
    range.value = String(live.opacity);
    value.textContent = `${live.opacity} %`;
    body.classList.toggle("off", !live.enabled);
    for (const c of controls()) c.disabled = c !== sw && !live.enabled;
  }

  // Pas de désactivation pendant l'enregistrement : le focus clavier reste sur le contrôle utilisé.
  let busy = false;
  async function save(change) {
    if (busy) return;
    busy = true;
    const result = await saveSettings(
      { liveCounter: change },
      status,
      messages,
    );
    busy = false;
    if (result) live = result.settings.liveCounter;
    draw();
  }

  sw.addEventListener("click", () => save({ enabled: !live.enabled }));
  section.append(body, status.node);
  draw();
  return section;
}
