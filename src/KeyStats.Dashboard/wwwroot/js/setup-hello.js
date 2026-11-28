// Étape « Windows Hello » de l'assistant de première installation : le choix, l'attente de
// l'invite de Windows pendant POST /api/setup, puis l'échec éventuel.

import { el } from "./states.js";
import { action, icon, intro } from "./setup-ui.js";

const texts = {
  failed: "Windows Hello n'a pas pu être activé.",
  failedHow:
    " La confirmation a été refusée ou la fenêtre fermée. Vous pouvez réessayer, ou continuer avec la seule phrase de secours et l'activer plus tard dans Réglages › Chiffrement.",
};

/** Zone d'attente : l'invite est dessinée par Windows, la page n'en montre qu'une évocation. */
function waiting() {
  const node = el("div", "setup-wait");
  node.setAttribute("role", "status");
  node.tabIndex = -1;
  const line = el("span", "setup-wait-title");
  line.append(
    el("span", "spinner"),
    "En attente de votre confirmation dans Windows…",
  );
  const sketch = el("div", "setup-sketch");
  sketch.setAttribute("aria-hidden", "true");
  sketch.append(
    el("span", "setup-sketch-title", "Invite système de Windows"),
    el("span"),
    el("span"),
    el("span"),
  );
  node.append(
    line,
    sketch,
    el(
      "span",
      "setup-wait-note",
      "Fenêtre dessinée par Windows, hors de l'application. Elle peut s'ouvrir derrière.",
    ),
  );
  return node;
}

/**
 * Étape 3. back : retour à l'étape 2 ; submit(hello) : envoi de la configuration, avec ou sans
 * Windows Hello. L'assistant appelle ensuite prompt(), puis failed(), failed(text) ou reset().
 */
export function helloStep(back, submit) {
  const { head, h1 } = intro(
    3,
    "Déverrouiller avec Windows Hello",
    "Windows Hello vous évite de taper la phrase de secours : visage, empreinte ou code PIN, selon cet ordinateur. La phrase reste valable dans tous les cas.",
  );
  const box = el("input");
  box.type = "checkbox";
  box.id = "setup-hello";
  box.checked = true;
  const choice = el("label", "setup-check");
  choice.htmlFor = box.id;
  const words = el("span", "setup-check-text");
  words.append(
    el(
      "span",
      "setup-check-title",
      "Utiliser Windows Hello pour déverrouiller",
    ),
    el(
      "span",
      null,
      "Vous pourrez le désactiver ou le réactiver dans Réglages.",
    ),
  );
  choice.append(box, words);

  const wait = waiting();
  const alert = el("div", "alert setup-alert");
  alert.setAttribute("role", "alert");

  // Réessayer renvoie la dernière demande : avec Hello après un refus, telle quelle après une panne.
  let wanted = true;
  const send = (hello) => () => submit((wanted = hello));
  // Revenir à la phrase efface l'échec : l'étape repart du choix.
  const retour = action("Retour", false, () => {
    show("choice");
    back();
  });
  const activate = action("Activer et continuer", true, send(true));
  const without = action("Continuer sans Windows Hello", false, send(false));
  const skip = action("Continuer sans", false, send(false));
  const retry = action("Réessayer", true, () => send(wanted)());
  const right = el("div", "setup-actions");
  const foot = el("div", "setup-foot");
  foot.append(retour, right);

  /** choice : avant l'envoi ; prompt : invite de Windows ouverte ; failed : Hello refusé. */
  function show(state, text) {
    const busy = state === "prompt";
    box.disabled = busy;
    retour.disabled = busy;
    choice.classList.toggle("setup-check-on", box.checked);
    wait.hidden = !busy;
    alert.hidden = state !== "failed";
    if (state === "failed") {
      const words = el("span");
      if (text) words.append(text);
      else words.append(el("strong", null, texts.failed), texts.failedHow);
      alert.replaceChildren(icon("error"), words);
    }
    right.replaceChildren(
      ...(busy
        ? []
        : state === "failed"
          ? wanted
            ? [skip, retry]
            : [retry]
          : [box.checked ? activate : without]),
    );
  }
  box.addEventListener("change", () => show("choice"));
  show("choice");

  return {
    body: [head, choice, wait, alert, foot],
    focus: h1,
    /** L'invite est ouverte ; le bouton cliqué a disparu, le focus passe à la zone d'attente. */
    prompt() {
      show("prompt");
      wait.focus();
    },
    /** Hello refusé (text absent) ou envoi impossible (text). Le focus va au bouton principal. */
    failed(text) {
      show("failed", text);
      retry.focus();
    },
    reset: () => show("choice"),
  };
}
