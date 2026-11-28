// Assistant de première installation : phrase de secours puis POST /api/setup. Il couvre toute la
// fenêtre tant que le chiffrement n'est pas configuré. Les phrases ne vivent que dans les champs,
// vidés dès la configuration faite.

import { postJson } from "./api.js";
import { el } from "./states.js";
import { done } from "./setup-done.js";
import { MinLength, phraseStep } from "./setup-phrase.js";
import { steps } from "./setup-ui.js";
import { welcome } from "./setup-welcome.js";

/**
 * Ouvre l'assistant dans #setup. onDone est appelé une fois le chiffrement configuré (ou trouvé déjà
 * configuré), assistant fermé, pour relire l'état de la clé.
 */
export function openSetup(onDone) {
  const root = document.getElementById("setup");
  const header = el("header", "setup-header");
  const brand = el("div", "setup-brand");
  brand.innerHTML =
    '<svg width="30" height="30" viewBox="0 0 30 30" aria-hidden="true"><rect class="brand-tile" x="1" y="1" width="28" height="28" rx="8"/><path d="M8 11h0M12 11h0M16 11h0M20 11h0M8 15h0M12 15h0M16 15h0M20 15h0M9 20h12" class="brand-keys" stroke-width="2.2" stroke-linecap="round"/></svg>';
  const name = el("span", null, "Statistiques de frappe ");
  name.append(el("span", "setup-muted", "· première installation"));
  brand.append(name);
  const list = el("ol", "setup-steps");
  list.setAttribute("aria-label", "Étapes");
  header.append(brand, list);
  const card = el("section", "setup-card");
  const main = el("main", "setup-main");
  main.append(card);
  root.replaceChildren(header, main);
  root.hidden = false;
  document.body.classList.add("setup-open");

  function show(n, step) {
    list.replaceChildren(
      ...steps.map((label, i) => {
        const li = el("li");
        if (i + 1 === n) li.setAttribute("aria-current", "step");
        else if (i + 1 < n) li.className = "setup-past";
        li.append(
          el("span", "setup-dot", i + 1 < n ? "✓" : String(i + 1)),
          label,
        );
        if (i + 1 < n) li.firstChild.setAttribute("aria-hidden", "true");
        return li;
      }),
    );
    card.classList.toggle("setup-card-wide", Boolean(step.wide));
    card.replaceChildren(...step.body);
    step.focus.focus();
  }

  function finish(hash) {
    root.hidden = true;
    root.replaceChildren();
    document.body.classList.remove("setup-open");
    if (hash) location.hash = hash;
    onDone();
  }

  const failure = (error) =>
    error.status
      ? `La configuration a échoué (erreur ${error.status}). Réessayez.`
      : "Le tableau de bord n'a pas répondu. Réessayez.";

  async function submit(passphrase) {
    phrase.busy(true);
    let result;
    try {
      result = await postJson("/api/setup", { passphrase, hello: false });
    } catch (error) {
      phrase.busy(false);
      phrase.fail(failure(error));
      return;
    }
    if (result.outcome === "phrase") {
      phrase.busy(false);
      phrase.fail(`Trop courte : ${MinLength} caractères minimum.`);
      return;
    }
    phrase.clear();
    // « already » : configuré entre-temps (autre fenêtre) ; l'assistant n'a plus rien à faire.
    if (result.outcome !== "done") return finish(null);
    show(4, done({ hello: false, restarted: result.agentRestarted }, finish));
  }

  const first = welcome(() => show(2, phrase));
  const phrase = phraseStep(() => show(1, first), submit);
  show(1, first);
}
