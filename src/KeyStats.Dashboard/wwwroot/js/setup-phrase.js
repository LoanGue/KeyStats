// Étape « Phrase de secours » de l'assistant de première installation : deux champs, leur
// vérification en direct et le message d'échec de l'envoi. La phrase ne quitte les champs que pour
// l'envoi, et clear() les vide une fois la configuration faite.

import { el } from "./states.js";
import { action, fieldMessage, icon, intro, note } from "./setup-ui.js";

/** Longueur minimale, comme KeySetup.MinPassphraseLength dans KeyStats.Core. */
export const MinLength = 12;

/**
 * Étape 2, construite une fois pour que Retour puis Commencer garde la saisie. back : retour à
 * l'étape 1 ; submit(passphrase) : envoi, une fois les deux champs valides et identiques.
 */
export function phraseStep(back, submit) {
  const { head, h1 } = intro(
    2,
    "Choisissez une phrase de secours",
    "Elle déverrouille vos données quand Windows Hello n'est pas disponible. Plusieurs mots ordinaires se retiennent mieux qu'un mot de passe compliqué.",
  );
  const field = (id, msgId) => {
    const input = el("input");
    input.id = id;
    input.type = "password";
    input.autocomplete = "new-password";
    input.spellcheck = false;
    input.setAttribute("aria-describedby", msgId);
    return input;
  };
  const first = field("setup-p1", "setup-p1-msg");
  const second = field("setup-p2", "setup-p2-msg");
  const counter = el("span", "setup-counter");
  const eye = el("button", "icon-btn setup-eye");
  eye.type = "button";
  eye.setAttribute("aria-label", "Afficher les phrases");
  eye.setAttribute("aria-pressed", "false");
  eye.append(icon("eye"));
  eye.addEventListener("click", () => {
    const shown = eye.getAttribute("aria-pressed") !== "true";
    eye.setAttribute("aria-pressed", String(shown));
    for (const input of [first, second])
      input.type = shown ? "text" : "password";
  });
  const short = fieldMessage(
    "setup-p1-msg",
    "bad",
    "error",
    `Trop courte : ${MinLength} caractères minimum.`,
  );
  const differ = fieldMessage(
    "setup-p2-msg",
    "bad",
    "error",
    "Les deux phrases ne correspondent pas.",
  );
  const same = fieldMessage(
    "setup-p2-ok",
    "ok",
    "check",
    "Les deux phrases correspondent.",
  );
  const alert = el("div", "alert setup-alert");
  alert.setAttribute("role", "alert");
  alert.hidden = true;

  // Le bouton œil reste hors du <label> : un clic dessus ne doit pas viser le champ.
  const label = (input, text) => {
    const node = el("label", null, text);
    node.htmlFor = input.id;
    return node;
  };
  counter.id = "setup-p1-count";
  first.setAttribute("aria-describedby", "setup-p1-count setup-p1-msg");
  const name1 = el("div", "setup-label");
  name1.append(label(first, "Phrase de secours"), counter);
  const row = el("div", "setup-row");
  row.append(first, eye);
  const label1 = el("div", "setup-field");
  label1.append(name1, row, short);
  second.setAttribute("aria-describedby", "setup-p2-msg setup-p2-ok");
  const name2 = el("div", "setup-label");
  name2.append(label(second, "Confirmez la phrase"));
  const label2 = el("div", "setup-field");
  label2.append(name2, second, differ, same);

  const go = action("Continuer", true, () => submit(first.value));
  const foot = el("div", "setup-foot");
  foot.append(action("Retour", false, back), go);

  function check() {
    const [a, b] = [first.value, second.value];
    const typed = a.length > 0 || b.length > 0;
    const tooShort = a.length > 0 && a.length < MinLength;
    const different = b.length > 0 && a !== b && !tooShort;
    const valid = a.length >= MinLength && a === b;
    counter.textContent = typed
      ? `${a.length} caractère${a.length > 1 ? "s" : ""} · ${MinLength} minimum`
      : `${MinLength} caractères minimum`;
    first.setAttribute("aria-invalid", String(tooShort));
    second.setAttribute("aria-invalid", String(different));
    first.classList.toggle("setup-valid", valid);
    second.classList.toggle("setup-valid", valid);
    short.hidden = !tooShort;
    differ.hidden = !different;
    same.hidden = !valid;
    go.disabled = !valid;
    alert.hidden = true;
  }
  for (const input of [first, second]) input.addEventListener("input", check);
  second.addEventListener("keydown", (event) => {
    if (event.key === "Enter" && !go.disabled) go.click();
  });
  check();

  return {
    body: [
      head,
      label1,
      label2,
      note(
        "warn",
        "warn",
        "Notez-la en lieu sûr. Sans elle et sans Windows Hello, les données chiffrées ne pourront pas être ouvertes.",
      ),
      alert,
      foot,
    ],
    focus: h1,
    busy(value) {
      go.disabled = value;
      if (!value) check();
    },
    fail(text) {
      alert.replaceChildren(icon("error"), el("span", null, text));
      alert.hidden = false;
      // Continuer, désactivé pendant l'envoi, a perdu le focus : il revient au premier champ.
      first.focus();
    },
    clear() {
      first.value = "";
      second.value = "";
    },
  };
}
