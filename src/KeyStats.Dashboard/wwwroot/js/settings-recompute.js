// Carte « Recalcul de l'historique » : relance l'analyse d'une période avec les seuils actuels
// (/api/recompute). Le recalcul tourne dans le tableau de bord ; la carte en suit l'avancement.

import { getJson, postJson } from "./api.js";
import {
  button,
  dateField,
  isoDay,
  settingsCard,
  showRefused,
  statusLine,
} from "./settings-card.js";
import { duration, isoDayLong, nf } from "./format.js";
import { el } from "./states.js";

const PollMs = 1000;

const texts = {
  dates: "Indiquez deux dates dans l'ordre, la seconde au plus hier.",
  locked:
    "Déverrouillez KeyStats pour lancer le recalcul : il lit le détail chiffré.",
  stopped:
    "Recalcul arrêté : KeyStats a été verrouillé. Les jours déjà traités sont recalculés.",
  failed:
    "Le recalcul a échoué. Les jours déjà traités sont recalculés ; réessayez pour les autres.",
};

const failure = (error) =>
  error.status
    ? `La demande a échoué (erreur ${error.status}). Réessayez.`
    : "Le tableau de bord n'a pas répondu. Réessayez.";

const days = (n) => `${nf.format(n)} jour${n > 1 ? "s" : ""}`;

/** Phrase de fin d'un recalcul, ou "" s'il n'y en a pas (aucun recalcul depuis l'ouverture). */
function summary(job) {
  const kept = job.skipped
    ? ` ${days(job.skipped)} gardé${job.skipped > 1 ? "s" : ""} tel${job.skipped > 1 ? "s" : ""} quel${job.skipped > 1 ? "s" : ""} : détail chiffré manquant ou incomplet, session étalée sur trop de jours ou qui se poursuit aujourd'hui.`
    : "";
  if (job.state === "done")
    return [
      `Recalcul terminé : ${days(job.done - job.skipped)} recalculé${job.done - job.skipped > 1 ? "s" : ""}.${kept}`,
    ];
  if (job.state === "cancelled")
    return [
      `Recalcul annulé après ${nf.format(job.done)} / ${days(job.total)}.${kept}`,
    ];
  if (job.state === "locked") return [texts.stopped, true];
  if (job.state === "failed") return [texts.failed, true];
  return [""];
}

/** ctx : contexte de la page (déverrouillage). */
export function recomputeCard(ctx) {
  const { section } = settingsCard(
    "Recalcul de l'historique",
    "Recalcule les statistiques d'une période avec les seuils actuels, à partir du détail chiffré : il demande donc le déverrouillage. Les frappes enregistrées ne sont pas modifiées.",
    "settings-wide",
  );

  const yesterday = new Date();
  yesterday.setDate(yesterday.getDate() - 1);
  const monthAgo = new Date(yesterday);
  monthAgo.setDate(yesterday.getDate() - 29);
  const max = isoDay(yesterday);
  const from = dateField("Du", isoDay(monthAgo), max);
  const to = dateField("Au", max, max);
  const run = button("Recalculer", true, start);
  const form = el("div", "settings-range recompute-form");
  form.append(from.row, to.row, run);

  const dayText = el("span", "recompute-day");
  const title = el("span", "recompute-title");
  title.append("Recalcul en cours… ", dayText);
  const count = el("span", "recompute-count");
  const top = el("div", "recompute-top");
  top.append(title, count);
  const bar = el("div", "recompute-bar");
  bar.setAttribute("role", "progressbar");
  bar.setAttribute("aria-label", "Progression du recalcul");
  bar.setAttribute("aria-valuemin", "0");
  bar.setAttribute("aria-valuemax", "100");
  const fill = el("span");
  bar.append(fill);
  const cancel = button("Annuler", false, stop);
  const bottom = el("div", "recompute-bottom");
  bottom.append(
    el(
      "span",
      null,
      "Vous pouvez continuer à utiliser l'application ; l'agent continue d'enregistrer.",
    ),
    cancel,
  );
  const panel = el("div", "recompute-panel");
  panel.append(top, bar, bottom);
  panel.hidden = true;

  const status = statusLine();
  section.append(form, panel, status.node);

  let timer = 0;

  function markDates(bad) {
    for (const input of [from.input, to.input])
      input.setAttribute("aria-invalid", String(input === bad));
  }
  for (const f of [from, to])
    f.input.addEventListener("input", () => markDates(null));

  /** Affiche l'état du recalcul ; relance le suivi tant qu'il tourne. */
  function show(job) {
    const running = job.state === "running";
    run.disabled = running;
    // Le panneau qui se masque ne doit pas emporter le focus (Annuler) : il revient sur Recalculer.
    if (!running && panel.contains(document.activeElement)) run.focus();
    panel.hidden = !running;
    clearTimeout(timer);
    if (!running) {
      const [text, bad] = summary(job);
      if (text) status.show(text, bad);
      return;
    }
    status.show("");
    dayText.textContent = job.day ? isoDayLong(job.day) : "";
    const parts = [`${nf.format(job.done)} / ${days(job.total)}`];
    if (job.remainingMs != null) {
      const left = duration(job.remainingMs);
      parts.push(
        `${left} restante${/^(1\s(min|h)|moins d'une minute)$/.test(left) ? "" : "s"}`,
      );
    }
    count.textContent = parts.join(" · ");
    const percent = job.total ? Math.round((job.done / job.total) * 100) : 0;
    fill.style.width = `${percent}%`;
    bar.setAttribute("aria-valuenow", String(percent));
    bar.setAttribute(
      "aria-valuetext",
      `${nf.format(job.done)} sur ${days(job.total)}`,
    );
    timer = setTimeout(poll, PollMs);
  }

  async function poll() {
    if (!section.isConnected) return;
    let job;
    try {
      job = await getJson("/api/recompute");
    } catch {
      // Une réponse manquée n'arrête pas le suivi : le recalcul continue côté tableau de bord.
      clearTimeout(timer);
      timer = setTimeout(poll, PollMs);
      return;
    }
    if (section.isConnected) show(job);
  }

  async function start() {
    const [a, b] = [from.input.value, to.input.value];
    const bad =
      !a || a > max ? from.input : !b || b > max || a > b ? to.input : null;
    markDates(bad);
    if (bad) {
      status.show(texts.dates, true);
      bad.focus();
      return;
    }
    run.disabled = true;
    let result;
    try {
      result = await postJson("/api/recompute", { from: a, to: b });
    } catch (error) {
      run.disabled = false;
      return status.show(
        error.status === 400 ? texts.dates : failure(error),
        true,
      );
    }
    if (!section.isConnected) return;
    if (result.outcome === "unreadable") return showRefused(status.node);
    if (result.outcome === "locked") {
      run.disabled = false;
      status.show(texts.locked, true);
      return ctx.unlock();
    }
    // started ou busy : le recalcul en cours est suivi, qu'il vienne de ce clic ou d'un précédent.
    show(result.job);
    cancel.focus();
  }

  async function stop() {
    cancel.disabled = true;
    try {
      show(await postJson("/api/recompute/cancel", {}));
    } catch (error) {
      status.show(failure(error), true);
    }
    cancel.disabled = false;
    if (!panel.hidden) cancel.focus();
  }

  // Un recalcul lancé avant l'ouverture de la page est repris tel quel.
  getJson("/api/recompute")
    .then((job) => section.isConnected && job.state === "running" && show(job))
    .catch(() => {});

  return section;
}
