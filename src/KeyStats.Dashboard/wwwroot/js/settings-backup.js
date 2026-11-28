// Carte « Dossier de sauvegarde » : dossier du miroir chiffré, choisi dans la fenêtre de Windows
// (/api/backup/folder), et sauvegarde immédiate (/api/backup/run). Le chemin ne vient jamais de la page.

import { postJson } from "./api.js";
import {
  button,
  settingsCard,
  showRefused,
  statusLine,
} from "./settings-card.js";
import { el } from "./states.js";

const when = new Intl.DateTimeFormat("fr-FR", {
  dateStyle: "long",
  timeStyle: "short",
});

const texts = {
  picking:
    "Choisissez le dossier dans la fenêtre de Windows. Elle peut s'ouvrir derrière cette fenêtre.",
  applied: "Dossier enregistré. Les prochaines sauvegardes y seront déposées.",
  pending:
    "Dossier enregistré. L'agent le prendra en compte à son prochain démarrage.",
  invalid:
    "Ce dossier ne convient pas : choisissez un dossier existant, autre que celui des données.",
  saveFailed: "Le fichier settings.json n'a pas pu être écrit. Réessayez.",
  running: "Sauvegarde en cours…",
  done: "Sauvegarde terminée.",
  busy: "Une sauvegarde est déjà en cours. Réessayez dans un instant.",
  runFailed:
    "La sauvegarde a échoué. Vérifiez que le dossier est accessible, puis réessayez.",
};

const failure = (error) =>
  error.status
    ? `La demande a échoué (erreur ${error.status}). Réessayez.`
    : "Le tableau de bord n'a pas répondu. Réessayez.";

/** backup : réponse de /api/backup, ou null si elle a échoué ; retry relit la page. */
export function backupCard(backup, retry) {
  const { section } = settingsCard(
    "Dossier de sauvegarde",
    "Une copie chiffrée de la base y est déposée.",
  );
  const status = statusLine();
  if (!backup) {
    const line = el("div", "settings-backup-line");
    line.append(
      el("span", "settings-off", "État de la sauvegarde indisponible."),
      button("Réessayer", false, retry),
    );
    section.append(line);
    return section;
  }

  const path = el("span", "settings-path");
  const choose = button("Choisir…", false, () =>
    act(choose, "/api/backup/folder", texts.picking, folderText),
  );
  const pick = el("div", "settings-backup-pick");
  pick.append(path, choose);

  const last = el("span", "settings-backup-last");
  const now = button("Sauvegarder maintenant", false, () =>
    act(now, "/api/backup/run", texts.running, runText),
  );
  const line = el("div", "settings-backup-line");
  line.append(last, now);
  section.append(pick, line, status.node);

  function show(view) {
    path.textContent = path.title = view.folder;
    last.replaceChildren(
      "Dernière sauvegarde : ",
      el(
        "span",
        null,
        view.lastMs == null ? "aucune" : when.format(new Date(view.lastMs)),
      ),
    );
  }

  // Une seule demande à la fois : la fenêtre de Windows et la copie se gênent l'une l'autre.
  async function act(trigger, route, waiting, describe) {
    choose.disabled = now.disabled = true;
    status.show(waiting);
    let result;
    try {
      result = await postJson(route, {});
    } catch (error) {
      choose.disabled = now.disabled = false;
      status.show(failure(error), true);
      if (!section.contains(document.activeElement)) trigger.focus();
      return;
    }
    choose.disabled = now.disabled = false;
    if (result.outcome === "unreadable") return showRefused(status.node);
    show(result.backup);
    const [text, bad] = describe(result);
    status.show(text, bad);
    if (!section.contains(document.activeElement)) trigger.focus();
  }

  show(backup);
  return section;
}

function folderText(result) {
  if (result.outcome === "cancelled") return [""];
  if (result.outcome === "saved")
    return [result.agentApplied ? texts.applied : texts.pending];
  if (result.outcome === "invalid") return [texts.invalid, true];
  return [texts.saveFailed, true];
}

function runText(result) {
  if (result.outcome === "done") return [texts.done];
  if (result.outcome === "busy") return [texts.busy];
  return [texts.runFailed, true];
}
