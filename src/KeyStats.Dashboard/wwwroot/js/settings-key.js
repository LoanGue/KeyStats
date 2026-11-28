// Carte « Chiffrement » : état du chiffrement, changement de la phrase de secours (/api/key/passphrase)
// et réactivation de Windows Hello (/api/key/hello). Aucune réponse ne contient de phrase ni de clé.

import { postJson } from "./api.js";
import { button, settingsCard, statusLine } from "./settings-card.js";
import { phraseForm } from "./settings-phrase.js";
import { el } from "./states.js";

const texts = {
  waiting:
    "Confirmez votre identité dans la fenêtre de Windows. Elle peut s'ouvrir derrière cette fenêtre.",
  done: "Windows Hello est activé.",
  refused:
    "Windows Hello n'a pas pu être activé. La confirmation a été refusée ou la fenêtre fermée.",
  locked: "Déverrouillez d'abord les données détaillées, puis réessayez.",
  setup: "Le chiffrement n'est pas encore configuré.",
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
  let open = false;
  const rows = el("div", "settings-rows");
  const status = statusLine();
  // Le bouton de réactivation disparaît une fois Windows Hello activé : le focus passe au message.
  status.node.tabIndex = -1;
  const note = el(
    "p",
    "settings-note",
    "Changer la phrase demande l'ancienne phrase ou Windows Hello.",
  );
  section.append(rows, status.node, note);

  // Une preuve déverrouille aussi la session : l'en-tête est relu. Le formulaire ouvert n'est redessiné
  // que si la carte change de forme, pour garder ses champs.
  function adopt(next, redraw = true) {
    const shape = (k) => `${k?.setup}${k?.hello}`;
    const changed = shape(next) !== shape(key);
    key = next;
    ctx.refreshKey();
    if (redraw || changed) draw();
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
      if (!section.isConnected) return;
      trigger.disabled = false;
      return status.show(failure(error), true);
    }
    if (!section.isConnected) return;
    trigger.disabled = false;
    const outcome = result.outcome;
    if (outcome === "locked") ctx.unlock();
    const known = {
      done: texts.done,
      locked: texts.locked,
      setup: texts.setup,
    };
    status.show(
      Object.hasOwn(known, outcome) ? known[outcome] : texts.refused,
      !Object.hasOwn(known, outcome),
    );
    adopt(result.key);
    // Le bouton a été redessiné : le focus le suit. Avec locked, le dialogue de déverrouillage l'a pris.
    if (outcome === "done") status.node.focus();
    else if (outcome !== "locked")
      section.querySelector(".settings-row .btn-primary")?.focus();
  }

  function draw() {
    const focused = section.contains(document.activeElement);
    rows.replaceChildren();
    // /api/key n'a pas répondu : l'état est inconnu, rien n'est affirmé.
    if (!key) {
      // render ne relit pas /api/key : l'état est relu d'abord.
      const retry = button("Réessayer", false, () =>
        ctx.refreshKey().then(ctx.retry),
      );
      const unknown = row("Chiffrement", retry);
      unknown.name.append(el("span", "settings-off", " · état indisponible"));
      rows.append(unknown.node);
      note.hidden = true;
      return;
    }
    if (!key.setup) {
      rows.append(
        row("Chiffrement", el("span", "settings-unit", "Pas encore configuré"))
          .node,
      );
      note.hidden = true;
      return;
    }
    note.hidden = false;
    rows.append(row("Chiffrement", on("Actif")).node);
    const change = button("Changer la phrase…", false, () => toggle(!open));
    change.setAttribute("aria-expanded", String(open));
    rows.append(row("Phrase de secours", change).node);
    if (open) {
      const form = phraseForm(key, {
        onKey: (next) => adopt(next, false),
        onClose: () => toggle(false),
      });
      rows.append(form.form);
      if (focused) form.focus();
    }
    const reenable = button("Réactiver Windows Hello", true, () =>
      enrol(reenable),
    );
    const hello = row("Windows Hello", key.hello ? on("Actif") : reenable);
    if (!key.hello)
      hello.name.append(el("span", "settings-off", " · désactivé"));
    rows.append(hello.node);
  }

  function toggle(value) {
    open = value;
    status.show("");
    draw();
    if (!open) section.querySelector("[aria-expanded]")?.focus();
  }

  draw();
  return section;
}
