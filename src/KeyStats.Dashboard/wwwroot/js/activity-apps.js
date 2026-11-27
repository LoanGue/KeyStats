// Carte « Répartition par application » de la page Activité : une ligne par appli, ses titres de fenêtre
// une fois la clé déverrouillée (maquette Activite).

import { ApiError, getJson } from "./api.js";
import { duration } from "./format.js";
import { el } from "./states.js";

const chevron =
  '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M9 6l6 6-6 6"/></svg>';
const lock =
  '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="5" y="11" width="14" height="10" rx="2"/><path d="M8 11V8a4 4 0 0 1 8 0v3"/></svg>';

// Appli ouverte, gardée d'un rendu à l'autre (déverrouillage, filtres) ; null : toutes fermées.
let open;
function line(label, ms) {
  const row = el("div", "activity-title");
  row.append(el("span", null, label), el("span", "activity-ms", duration(ms)));
  return row;
}

function lockPanel(ctx) {
  const box = el("div", "activity-lock");
  const text = el("span");
  // Contenu fixe de ce module, jamais de données reçues.
  text.innerHTML = lock;
  text.append("Les titres de fenêtre sont chiffrés.");
  const button = el("button", "btn btn-primary btn-sm", "Déverrouiller");
  button.type = "button";
  button.addEventListener("click", () => ctx.unlock());
  box.append(text);
  if (ctx.key?.hello || ctx.key?.passphrase) box.append(button);
  return box;
}

function titleList(list) {
  const box = el("div", "activity-titles");
  for (const t of list.titles) box.append(line(t.title, t.writingMs));
  if (list.otherMs) box.append(line("Autres titres", list.otherMs));
  if (list.untitledMs) box.append(line("Sans titre", list.untitledMs));
  if (!box.childElementCount)
    box.append(el("p", "activity-note", "Aucun titre de fenêtre."));
  return box;
}

// `titles` : titres déjà lus pendant ce rendu ; un nouveau rendu (verrouillage, filtres) repart de zéro.
async function fillTitles(detail, app, ctx, titles) {
  const url = `/api/activity/titles${ctx.query || "?"}${ctx.query ? "&" : ""}target=${app.id}`;
  detail.replaceChildren(el("p", "activity-note", "Lecture des titres…"));
  let list = titles.get(url);
  if (!list) {
    try {
      list = await getJson(url);
    } catch (error) {
      if (!ctx.isCurrent() || !detail.isConnected || detail.hidden) return;
      detail.replaceChildren(
        error instanceof ApiError && error.status === 403
          ? lockPanel(ctx)
          : el("p", "activity-note", "Les titres n'ont pas pu être lus."),
      );
      return;
    }
    // Une autre page, d'autres filtres ou une autre appli ouverte entre-temps.
    if (!ctx.isCurrent() || !detail.isConnected || detail.hidden) return;
    titles.set(url, list);
  }
  detail.replaceChildren(titleList(list));
}

function appRow(app, max, ctx, rows, titles) {
  const row = el("div", "activity-app");
  const button = el("button", "activity-app-head");
  button.type = "button";
  const icon = el("span", "activity-chevron");
  icon.innerHTML = chevron;
  const bar = el("span", "activity-bar");
  bar.setAttribute("aria-hidden", "true");
  bar.style.width = `${(app.writingMs / max) * 100}%`;
  const track = el("span", "activity-track");
  track.append(bar);
  button.append(
    icon,
    el("span", "activity-name", app.name),
    track,
    el("span", "activity-ms", duration(app.writingMs)),
  );
  const detail = el("div", "activity-detail");
  const show = (expanded) => {
    button.setAttribute("aria-expanded", String(expanded));
    detail.hidden = !expanded;
    if (!expanded) return;
    if (ctx.key?.unlocked) fillTitles(detail, app, ctx, titles);
    else detail.replaceChildren(lockPanel(ctx));
  };
  button.addEventListener("click", () => {
    const expanded = button.getAttribute("aria-expanded") !== "true";
    open = expanded ? app.id : null;
    for (const other of rows) if (other !== show) other(false);
    show(expanded);
  });
  rows.push(show);
  row.append(button, detail);
  return { row, show };
}

export function appsCard(apps, ctx) {
  const list = el("div", "activity-apps");
  const max = Math.max(...apps.map((a) => a.writingMs), 1);
  const rows = [];
  const titles = new Map();
  // L'appli ouverte peut manquer sous d'autres filtres : on revient alors à la première.
  const first =
    open === null || apps.some((a) => a.id === open) ? open : apps[0]?.id;
  for (const app of apps) {
    const { row, show } = appRow(app, max, ctx, rows, titles);
    list.append(row);
    show(app.id === first);
  }
  return {
    title: "Répartition par application",
    note: "Temps d'écriture. Ouvrez une application pour ses titres de fenêtre.",
    body: list,
  };
}
