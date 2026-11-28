// Formulaire « Changer la phrase » de la carte Chiffrement : preuve (Windows Hello ou phrase actuelle),
// nouvelle phrase et confirmation. Les phrases ne quittent les champs que pour la requête, puis sont effacées.

import { postJson } from "./api.js";
import { button, statusLine } from "./settings-card.js";
import { el } from "./states.js";

const minLength = 12;

const texts = {
  short: "Trop courte : 12 caractères minimum.",
  mismatch: "Les deux phrases ne correspondent pas.",
  current: "Saisissez la phrase actuelle.",
  wrong: "Phrase incorrecte. Vérifiez Verr. Maj et la disposition du clavier.",
  hello:
    "Windows Hello n'a pas confirmé votre identité, ou la fenêtre a été fermée.",
  waiting:
    "Confirmez votre identité dans la fenêtre de Windows. Elle peut s'ouvrir derrière cette fenêtre.",
  done: "Phrase changée. Les sauvegardes faites avant ce changement s'ouvrent encore avec l'ancienne phrase.",
};

/** Même forme que KeyWrap.NormalizePassphrase : NFC, bords retirés, suites d'espaces réduites. */
const normalized = (s) => s.normalize("NFC").trim().split(/\s+/).join(" ");

const failure = (error) =>
  error.status
    ? `La demande a échoué (erreur ${error.status}). Réessayez.`
    : "Le tableau de bord n'a pas répondu. Réessayez.";

let fieldId = 0;

function field(label) {
  const id = `settings-phrase-${++fieldId}`;
  const wrap = el("div", "settings-phrase-field");
  const name = el("label", null, label);
  name.htmlFor = id;
  const input = el("input");
  input.id = id;
  input.type = "password";
  input.autocomplete = "off";
  input.spellcheck = false;
  const error = el("span", "settings-phrase-error");
  error.id = `${id}-error`;
  error.hidden = true;
  input.setAttribute("aria-describedby", error.id);
  wrap.append(name, input, error);
  const show = (text) => {
    error.textContent = text || "";
    error.hidden = !text;
    input.setAttribute("aria-invalid", String(Boolean(text)));
    if (text) input.focus();
  };
  return { wrap, input, show };
}

/** Choix de la preuve quand les deux sont possibles ; sinon la seule disponible. */
function proofChoice(key, onChange) {
  const box = el("fieldset", "settings-proof");
  box.append(el("legend", null, "Confirmer avec"));
  const options = [
    ["hello", "Windows Hello", key.hello],
    ["passphrase", "La phrase actuelle", key.passphrase],
  ].filter(([, , on]) => on);
  for (const [value, label] of options) {
    const line = el("label", "settings-proof-option");
    const radio = el("input");
    radio.type = "radio";
    radio.name = `settings-proof-${fieldId}`;
    radio.value = value;
    radio.checked = value === options[0][0];
    radio.addEventListener("change", () => onChange(value));
    line.append(radio, el("span", null, label));
    box.append(line);
  }
  box.hidden = options.length < 2;
  return { box, first: options[0]?.[0] };
}

/** onKey reçoit l'état de la clé renvoyé ; onClose ferme le formulaire. */
export function phraseForm(key, { onKey, onClose }) {
  const form = el("form", "settings-phrase");
  const current = field("Phrase actuelle");
  const next = field("Nouvelle phrase de secours");
  const again = field("Confirmez la phrase");
  const choice = proofChoice(key, (value) => showProof(value));
  let proof = choice.first;
  const status = statusLine();
  const submit = button("Changer la phrase", true);
  submit.type = "submit";
  const actions = el("div", "settings-actions");
  actions.append(button("Annuler", false, onClose), submit);
  form.append(
    choice.box,
    current.wrap,
    next.wrap,
    again.wrap,
    status.node,
    actions,
  );

  function showProof(value) {
    proof = value;
    current.wrap.hidden = value !== "passphrase";
    current.show(null);
  }
  showProof(proof);

  function check() {
    for (const f of [current, next, again]) f.show(null);
    // Le premier champ fautif, dans l'ordre du formulaire, reçoit le message et le focus.
    if (proof === "passphrase" && !current.input.value)
      return current.show(texts.current);
    if (normalized(next.input.value).length < minLength)
      return next.show(texts.short);
    if (normalized(again.input.value) !== normalized(next.input.value))
      return again.show(texts.mismatch);
    return true;
  }

  async function send() {
    const body = { method: proof, newPassphrase: next.input.value };
    if (proof === "passphrase") body.passphrase = current.input.value;
    current.input.value = "";
    status.show(proof === "hello" ? texts.waiting : "");
    let result;
    try {
      result = await postJson("/api/key/passphrase", body);
    } catch (error) {
      status.show(failure(error), true);
      return;
    }
    if (!form.isConnected) return;
    onKey(result.key);
    if (result.outcome === "done") {
      next.input.value = again.input.value = "";
      status.show(texts.done);
    } else if (result.outcome === "phrase") {
      status.show("");
      next.show(texts.short);
    } else if (result.outcome === "proof" && proof === "passphrase") {
      status.show("");
      current.show(texts.wrong);
    } else status.show(texts.hello, true);
  }

  form.addEventListener("submit", async (event) => {
    event.preventDefault();
    if (submit.disabled || check() !== true) return;
    submit.disabled = true;
    await send();
    submit.disabled = false;
  });
  return {
    form,
    focus: () => (proof === "passphrase" ? current : next).input.focus(),
  };
}
