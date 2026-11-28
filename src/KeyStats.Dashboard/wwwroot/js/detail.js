// Page Détail à la milliseconde : une session rejouée touche par touche (/api/detail, puis
// /api/detail/keys). Les frappes ne sont lues qu'une fois les données déverrouillées.

import { ApiError, getJson } from "./api.js";
import { sessionsCard, sessionTitle } from "./detail-sessions.js";
import { timeline } from "./detail-timeline.js";
import { nf } from "./format.js";
import { el, showEmptyPeriod } from "./states.js";

const lockIcon =
  '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="5" y="11" width="14" height="10" rx="2"/><path d="M8 11V8a4 4 0 0 1 8 0v3"/></svg>';

function lockCard(ctx) {
  const box = el("section", "card detail-lock");
  const icon = el("span", "detail-lock-icon");
  // Contenu fixe de ce module, jamais de données reçues.
  icon.innerHTML = lockIcon;
  const text = el("div");
  text.append(
    el("h2", null, "Cette vue demande le déverrouillage"),
    el(
      "p",
      null,
      "Le détail touche par touche est chiffré sur cet ordinateur. Déverrouillez pour le consulter ; il se reverrouille quand vous cliquez sur « Verrouiller ».",
    ),
  );
  box.append(icon, text);
  if (ctx.key?.hello) {
    const hello = el(
      "button",
      "btn btn-primary",
      "Déverrouiller avec Windows Hello",
    );
    hello.type = "button";
    hello.addEventListener("click", () => ctx.unlock());
    box.append(hello);
  }
  if (ctx.key?.passphrase) {
    const phrase = el("button", "detail-link", "Utiliser la phrase de secours");
    phrase.type = "button";
    phrase.addEventListener("click", () => ctx.unlock("phrase"));
    box.append(phrase);
  }
  if (!ctx.key?.hello && !ctx.key?.passphrase)
    text.append(
      el(
        "p",
        null,
        "Aucun moyen de déverrouillage n'est disponible pour le moment. La page Réglages indique l'état du chiffrement.",
      ),
    );
  const wrap = el("div", "detail-lock-wrap");
  wrap.append(box);
  return wrap;
}

const frameTitle = "Frise touche par touche";

/** Carte de la frise : en-tête, puis contenu (message ou frise). */
function frame(note, aside) {
  const section = el("section", "card chart-card detail-frame");
  const head = el("div", "chart-head");
  const text = el("div");
  const p = el("p");
  p.append(note);
  text.append(el("h2", null, frameTitle), p);
  head.append(text);
  if (aside) head.append(aside);
  section.append(head);
  return section;
}

function footnotes(keys) {
  const notes = [];
  if (keys.truncated)
    notes.push(
      `Seuls les ${nf.format(keys.presses.length)} premiers appuis de la session sont tracés.`,
    );
  if (keys.incomplete && keys.presses.length)
    notes.push(
      "Une partie des frappes de cette session n'a pas pu être relue : la frise peut être incomplète.",
    );
  return notes.map((t) => el("p", "chart-footnote", t));
}

function keysCard(session, keys) {
  const title = sessionTitle(session);
  if (!keys.presses.length) {
    const section = frame(title);
    section.append(
      el(
        "p",
        "chart chart-missing",
        "Aucune frappe de cette session n'a pu être relue : elle a été enregistrée sans le détail chiffré, ou celui-ci est illisible.",
      ),
    );
    return section;
  }
  const t = timeline(keys.presses, keys.endMs - keys.startMs, title);
  const section = frame(t.note, t.aside);
  section.append(t.body, ...footnotes(keys));
  return section;
}

export async function renderDetail(view, ctx) {
  if (!ctx.key?.unlocked) {
    view.replaceChildren(lockCard(ctx));
    return;
  }
  const data = await getJson(`/api/detail${ctx.query}`);
  if (!ctx.isCurrent()) return;
  if (!data.sessions.length) {
    showEmptyPeriod(view, ctx);
    return;
  }
  const grid = el("div", "detail-grid");
  let slot = frame(sessionTitle(data.sessions[0]));
  // Frappes déjà relues pendant ce rendu ; un nouveau rendu (verrouillage, filtres) repart de zéro.
  const read = new Map();
  let asked = 0;
  const place = (card) => {
    slot.replaceWith(card);
    slot = card;
  };

  async function show(session) {
    const id = ++asked;
    let keys = read.get(session.id);
    if (!keys) {
      const waiting = frame(sessionTitle(session));
      waiting.append(el("p", "chart chart-missing", "Lecture des frappes…"));
      place(waiting);
      try {
        keys = await getJson(
          `/api/detail/keys?session=${encodeURIComponent(session.id)}`,
        );
      } catch (error) {
        if (!ctx.isCurrent() || id !== asked) return;
        if (error instanceof ApiError && error.status === 403) {
          // Verrouillé entre-temps : la page repasse à l'état verrouillé.
          try {
            await ctx.refreshKey();
          } catch {
            // État inconnu : le message d'échec ci-dessous suffit.
          }
          if (!ctx.isCurrent() || id !== asked) return;
          if (!ctx.key?.unlocked) {
            ctx.retry();
            return;
          }
        }
        const failed = frame(sessionTitle(session));
        failed.append(
          el(
            "p",
            "chart chart-missing",
            "Les frappes de cette session n'ont pas pu être lues.",
          ),
        );
        place(failed);
        return;
      }
      if (!ctx.isCurrent()) return;
      // Gardées même pour un choix dépassé : rechoisir la session ne relit pas les frappes.
      read.set(session.id, keys);
      if (id !== asked) return;
    }
    place(keysCard(session, keys));
  }

  // slot est dans la grille avant la liste, qui choisit sa première session pendant sa construction.
  grid.append(slot);
  grid.prepend(sessionsCard(data, show));
  view.replaceChildren(grid);
}
