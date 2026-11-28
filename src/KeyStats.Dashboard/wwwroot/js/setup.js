// Assistant de première installation : phrase de secours, Windows Hello, puis POST /api/setup. Il
// couvre toute la fenêtre tant que le chiffrement n'est pas configuré. Les phrases ne vivent que dans les champs,
// vidés dès la configuration faite.

import { postJson } from "./api.js";
import { el } from "./states.js";
import { done } from "./setup-done.js";
import { helloStep } from "./setup-hello.js";
import { MinLength } from "./passphrase.js";
import { phraseStep } from "./setup-phrase.js";
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

  /** skipped : numéros des étapes passées sans être faites, qui gardent leur numéro sans coche. */
  function show(n, step, skipped = []) {
    list.replaceChildren(
      ...steps.map((label, i) => {
        const li = el("li");
        const past = i + 1 < n && !skipped.includes(i + 1);
        if (i + 1 === n) li.setAttribute("aria-current", "step");
        else if (past) li.className = "setup-past";
        li.append(el("span", "setup-dot", past ? "✓" : String(i + 1)), label);
        if (past) li.firstChild.setAttribute("aria-hidden", "true");
        else if (i + 1 < n) li.append(el("span", "sr-only", " (non faite)"));
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

  // Étape 3 : la phrase saisie à l'étape 2 part avec le choix de Windows Hello.
  async function submit(hello) {
    hello3.prompt();
    let result;
    try {
      result = await postJson("/api/setup", {
        passphrase: phrase.value(),
        hello,
      });
    } catch (error) {
      return hello3.failed(failure(error));
    }
    if (result.outcome === "hello") return hello3.failed();
    if (result.outcome === "phrase") {
      hello3.reset();
      show(2, phrase);
      return phrase.fail(`Trop courte : ${MinLength} caractères minimum.`);
    }
    phrase.clear();
    // « already » : configuré entre-temps (autre fenêtre) ; l'assistant n'a plus rien à faire.
    if (result.outcome !== "done") return finish(null);
    // Sans Windows Hello, son étape ne doit pas paraître faite.
    const card = done({ hello, restarted: result.agentRestarted }, finish);
    show(4, card, hello ? [] : [3]);
  }

  const first = welcome(() => show(2, phrase));
  const phrase = phraseStep(
    () => show(1, first),
    () => show(3, hello3),
  );
  const hello3 = helloStep(() => show(2, phrase), submit);
  show(1, first);
}
