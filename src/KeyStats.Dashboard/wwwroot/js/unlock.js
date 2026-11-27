// Déverrouillage des données chiffrées : boutons de l'en-tête et dialogue (Windows Hello ou phrase de secours).
// La clé reste côté tableau de bord ; la page n'envoie que la phrase saisie et ne la garde pas.

import { postJson } from "./api.js";

const $ = (id) => document.getElementById(id);

const steps = {
  invite: {
    title: "Déverrouiller les données détaillées",
    text: "Cette vue affiche des données chiffrées : frappes touche par touche et titres de fenêtre.",
    main: "Utiliser Windows Hello",
  },
  waiting: {
    title: "En attente de Windows Hello…",
    text: "Confirmez votre identité dans la fenêtre de Windows (visage, empreinte ou code PIN). Elle peut s'ouvrir derrière cette fenêtre.",
  },
  failed: {
    title: "Déverrouiller les données détaillées",
    alert:
      "Windows Hello n'a pas confirmé votre identité, ou la fenêtre a été fermée.",
    main: "Réessayer",
  },
  phrase: { title: "Phrase de secours", main: "Déverrouiller" },
};

/** Branche les boutons de l'en-tête ; open(key) ouvre le dialogue, onChange suit chaque changement de clé. */
export function initUnlock(getKey, onChange) {
  const dialog = $("unlock-dialog");
  const input = $("unlock-input");
  let key = null;
  let step = "invite";
  // Une réponse arrivée après Annuler ne doit pas redessiner un dialogue rouvert depuis.
  let request = 0;

  function show(next, alert) {
    step = next;
    const s = steps[next];
    $("unlock-title").textContent = s.title;
    $("unlock-text").textContent = s.text || "";
    $("unlock-text").hidden = !s.text;
    $("unlock-icon").hidden = next !== "invite" && next !== "waiting";
    $("unlock-lock").hidden = next !== "invite";
    $("unlock-spin").hidden = next !== "waiting";
    $("unlock-status").setAttribute("aria-busy", String(next === "waiting"));
    const message = alert || s.alert;
    $("unlock-alert").hidden = !message;
    $("unlock-alert-text").textContent = message || "";
    $("unlock-phrase").hidden = next !== "phrase";
    $("unlock-main").hidden = !s.main;
    $("unlock-main-text").textContent = s.main || "";
    $("unlock-face").hidden = next !== "invite";
    const other = next === "phrase" ? key.hello : key.passphrase;
    $("unlock-switch").hidden = next === "waiting" || !other;
    $("unlock-switch").textContent =
      next === "phrase"
        ? "Revenir à Windows Hello"
        : "Utiliser la phrase de secours";
    phraseError(false);
    if (next === "phrase") input.focus();
    else $(s.main ? "unlock-main" : "unlock-cancel").focus();
  }

  function phraseError(on) {
    $("unlock-error").hidden = !on;
    input.setAttribute("aria-invalid", String(on));
  }

  async function send(body) {
    const id = ++request;
    try {
      const state = await postJson("/api/key/unlock", body);
      if (id !== request) return onChange();
      if (state.unlocked) {
        dialog.close();
        return onChange();
      }
      if (body.method === "hello") show("failed");
      else {
        show("phrase");
        phraseError(true);
      }
    } catch (error) {
      if (id === request)
        show(
          body.method === "hello" ? "failed" : "phrase",
          `La demande a échoué : ${error.message}`,
        );
    }
  }

  function hello() {
    show("waiting");
    send({ method: "hello" });
  }

  function submitPhrase(event) {
    event.preventDefault();
    const passphrase = input.value;
    input.value = "";
    if (!passphrase) return phraseError(true);
    $("unlock-main").disabled = true;
    send({ method: "passphrase", passphrase }).finally(() => {
      $("unlock-main").disabled = false;
    });
  }

  function open() {
    key = getKey();
    if (!key || key.unlocked || !(key.hello || key.passphrase)) return;
    dialog.showModal();
    show(key.hello ? "invite" : "phrase");
  }

  $("unlock-main").addEventListener("click", (event) => {
    if (step === "phrase") submitPhrase(event);
    else hello();
  });
  $("unlock-phrase").addEventListener("submit", submitPhrase);
  $("unlock-switch").addEventListener("click", () =>
    show(step === "phrase" ? "invite" : "phrase"),
  );
  $("unlock-cancel").addEventListener("click", () => dialog.close());
  dialog.addEventListener("close", () => {
    request++;
    input.value = "";
    input.type = "password";
    $("unlock-eye").setAttribute("aria-pressed", "false");
  });
  $("unlock-eye").addEventListener("click", () => {
    const shown = input.type === "password";
    input.type = shown ? "text" : "password";
    $("unlock-eye").setAttribute("aria-pressed", String(shown));
    input.focus();
  });

  $("unlock-open").addEventListener("click", open);
  $("key-lock").addEventListener("click", async () => {
    $("key-lock").disabled = true;
    try {
      await postJson("/api/key/lock", {});
    } catch {
      // L'état relu ensuite montre si les données sont restées déverrouillées.
    } finally {
      $("key-lock").disabled = false;
      onChange();
    }
  });
  return open;
}
