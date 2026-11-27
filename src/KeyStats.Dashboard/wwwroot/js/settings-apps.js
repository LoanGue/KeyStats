// Carte « Applications exclues » : noms d'exécutables, ajoutés par le sélecteur de fichiers de Windows.

import { el } from "./states.js";
import {
  button,
  saveSettings,
  settingsCard,
  statusLine,
} from "./settings-card.js";

const messages = {
  applied: "Enregistré. L'agent applique la liste dès maintenant.",
  pending: "Enregistré. L'agent appliquera la liste à son prochain démarrage.",
};

// Seul le nom du fichier compte : l'agent compare le nom de l'exécutable au premier plan.
const appName = (exe) => exe.replace(/\.exe$/i, "");

function emptyBox() {
  const box = el("div", "settings-empty");
  box.append(
    el("span", "settings-empty-title", "Aucune application exclue"),
    el(
      "span",
      null,
      "Ajoutez par exemple votre gestionnaire de mots de passe.",
    ),
  );
  return box;
}

export function appsCard(settings, locked) {
  const { section } = settingsCard(
    "Applications exclues",
    "L'agent n'enregistre rien quand l'une d'elles est au premier plan.",
  );
  const list = el("ul", "settings-apps");
  const status = statusLine();
  let apps = settings.excludedApps;

  const picker = el("input");
  picker.type = "file";
  picker.accept = ".exe";
  picker.hidden = true;
  picker.tabIndex = -1;
  const add = button("Ajouter une application…", false, () => picker.click());

  async function save(next) {
    for (const b of section.querySelectorAll("button")) b.disabled = true;
    const result = await saveSettings({ excludedApps: next }, status, messages);
    if (result) apps = result.settings.excludedApps;
    draw();
  }

  function draw() {
    const rows = apps.map((exe) => {
      const row = el("li", "settings-app");
      const icon = el("span", "settings-app-icon");
      icon.setAttribute("aria-hidden", "true");
      const name = el("span", "settings-app-name", appName(exe));
      name.append(el("span", "settings-app-exe", ` · ${exe}`));
      const remove = button("Retirer", false, () =>
        save(apps.filter((a) => a !== exe)),
      );
      remove.classList.add("btn-sm");
      remove.setAttribute("aria-label", `Retirer ${exe}`);
      remove.disabled = locked;
      row.append(icon, name, remove);
      return row;
    });
    list.replaceChildren(...rows);
    list.hidden = !apps.length;
    empty.hidden = apps.length > 0;
    add.disabled = locked;
  }

  picker.addEventListener("change", () => {
    const file = picker.files && picker.files[0];
    picker.value = "";
    if (!file) return;
    const lower = file.name.toLowerCase();
    if (!lower.endsWith(".exe")) {
      status.show("Choisissez un fichier .exe.", true);
      return;
    }
    if (apps.some((a) => a.toLowerCase() === lower)) {
      status.show(`${file.name} est déjà exclue.`);
      return;
    }
    save([...apps, file.name]);
  });

  const empty = emptyBox();
  const actions = el("div", "settings-actions start");
  actions.append(add, picker);
  section.append(empty, list, actions, status.node);
  draw();
  return section;
}
