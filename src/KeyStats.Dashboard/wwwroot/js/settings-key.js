// Carte « Chiffrement » : état du chiffrement et réactivation de Windows Hello (/api/key/hello).
// Aucune réponse ne contient de phrase ni de clé.

import { postJson } from "./api.js";
import { button, settingsCard, statusLine } from "./settings-card.js";
import { el } from "./states.js";

const texts = {
  waiting:
    "Confirmez votre identité dans la fenêtre de Windows. Elle peut s'ouvrir derrière cette fenêtre.",
  done: "Windows Hello est activé.",
  refused:
    "Windows Hello n'a pas pu être activé. La confirmation a été refusée ou la fenêtre fermée.",
  locked: "Déverrouillez d'abord les données détaillées, puis réessayez.",
};

const failure = (error) =>
  error.status
    ? `La demande a échoué (erreur ${error.status}). Réessayez.`
    : "Le tableau de bord n'a pas répondu. Réessayez.";

function on(label) {
  const node = el("span", "settings-on");
  node.innerHTML =
    '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 12.5l4.5 4.5L19 7.5"/></svg>';
  node.append(label);
  return node;
}

function row(label, value) {
  const node = el("div", "settings-row");
  const name = el("span", null, label);
  node.append(name, value);
  return { node, name };
}

export function keyCard(ctx) {
  const { section } = settingsCard(
    "Chiffrement",
    "Le détail des frappes et les titres de fenêtre sont chiffrés.",
  );
  let key = ctx.key;
  const rows = el("div", "settings-rows");
  const status = statusLine();
  // Le bouton de réactivation disparaît une fois Windows Hello activé : le focus passe au message.
  status.node.tabIndex = -1;
  section.append(rows, status.node);

  function adopt(next) {
    key = next;
    ctx.refreshKey();
    draw();
  }

  async function enrol(trigger) {
    if (!key.unlocked) {
      status.show(texts.locked);
      return ctx.unlock();
    }
    trigger.disabled = true;
    status.show(texts.waiting);
    let result;
    try {
      result = await postJson("/api/key/hello", {});
    } catch (error) {
      trigger.disabled = false;
      return status.show(failure(error), true);
    }
    if (!section.isConnected) return;
    trigger.disabled = false;
    const outcome = result.outcome;
    if (outcome === "locked") ctx.unlock();
    status.show(
      { done: texts.done, locked: texts.locked }[outcome] ?? texts.refused,
      outcome !== "done" && outcome !== "locked",
    );
    adopt(result.key);
    // Le bouton a été redessiné : le focus le suit. Avec locked, le dialogue de déverrouillage l'a pris.
    if (outcome === "done") status.node.focus();
    else if (outcome !== "locked")
      section.querySelector(".settings-row .btn-primary")?.focus();
  }

  function draw() {
    rows.replaceChildren();
    if (!key || !key.setup) {
      rows.append(
        row("Chiffrement", el("span", "settings-unit", "Pas encore configuré"))
          .node,
      );
      return;
    }
    rows.append(row("Chiffrement", on("Actif")).node);
    const reenable = button("Réactiver Windows Hello", true, () =>
      enrol(reenable),
    );
    const hello = row("Windows Hello", key.hello ? on("Actif") : reenable);
    if (!key.hello)
      hello.name.append(el("span", "settings-off", " · désactivé"));
    rows.append(hello.node);
  }

  draw();
  return section;
}
