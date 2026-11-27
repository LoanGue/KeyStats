// Filtres de l'en-tête. Leur état vit dans l'adresse (#/page?period=…), donc un rechargement le garde.
// Noms et valeurs : ceux que lit StatsFilter côté Core.

import { getJson } from "./api.js";

const periods = [
  ["day", "Jour"],
  ["week", "Semaine"],
  ["month", "Mois"],
  ["year", "Année"],
  ["all", "Tout"],
];
const kinds = [
  ["", "Tout"],
  ["prose", "Texte"],
  ["code", "Code"],
  ["mixed", "Mixte"],
];
const keys = ["period", "app", "kind", "device"];
let options = null;

const listed = (pairs, value) => pairs.some(([v]) => v === value);
const knownId = (items, value) => items.some((x) => String(x.id) === value);

// Une adresse ancienne ou tapée à la main ne doit pas faire répondre 400 à l'API :
// une valeur inconnue est ignorée, comme le filtre clavier quand il est masqué.
function accepts(key, value) {
  const apps = options ? options.apps : [];
  const devices = options ? options.devices : [];
  if (key === "period") return value !== "week" && listed(periods, value);
  if (key === "kind") return listed(kinds, value);
  if (key === "app") return knownId(apps, value);
  return devices.length >= 2 && knownId(devices, value);
}

/** Filtres lus dans la partie requête du fragment, sans valeur inconnue. */
export function readFilters(params) {
  const filters = {};
  for (const key of keys) {
    const value = params.get(key);
    if (value && accepts(key, value)) filters[key] = value;
  }
  return filters;
}

/** Requête pour l'API, à partir de filtres lus par readFilters. */
export function filterQuery(filters) {
  const params = new URLSearchParams();
  for (const key of keys) if (filters[key]) params.set(key, filters[key]);
  const text = params.toString();
  return text ? `?${text}` : "";
}

function segment(container, options, current, onPick) {
  container.replaceChildren(
    ...options.map(([value, label]) => {
      const node = document.createElement("button");
      node.type = "button";
      node.textContent = label;
      node.setAttribute("aria-pressed", String(value === current));
      node.addEventListener("click", () => onPick(value));
      return node;
    }),
  );
}

/** Liste des applications et des claviers, lue une fois par ouverture de la fenêtre. */
export async function loadOptions() {
  options = await getJson("/api/filters");
  return options;
}

/** Nom d'une application d'après /api/filters, ou son numéro quand elle n'y figure pas. */
export function appName(id) {
  const app = options ? options.apps.find((a) => a.id === id) : null;
  return app ? app.name : `Application ${id}`;
}

/** Dessine les filtres ; onChange reçoit les nouveaux filtres. */
export function renderFilters(root, filters, onChange) {
  const set = (key, value) => {
    const next = { ...filters };
    if (value) next[key] = value;
    else delete next[key];
    onChange(next);
  };
  segment(
    root.querySelector('[data-filter="period"]'),
    periods,
    filters.period || "week",
    (v) => set("period", v === "week" ? "" : v),
  );
  segment(
    root.querySelector('[data-filter="kind"]'),
    kinds,
    filters.kind || "",
    (v) => set("kind", v),
  );

  const apps = options ? options.apps : [];
  const select = root.querySelector("#f-app");
  const all = new Option("Toutes les applications", "");
  select.replaceChildren(
    all,
    ...apps.map((app) => new Option(app.name, String(app.id))),
  );
  select.value = filters.app || "";
  select.onchange = () => set("app", select.value);

  // Un seul clavier connu : le filtre n'apporterait rien.
  const devices = options ? options.devices : [];
  root.querySelector("#device-field").hidden = devices.length < 2;
  const choices = [["", "Tous"], ...devices.map((d) => [String(d.id), d.name])];
  segment(
    root.querySelector('[data-filter="device"]'),
    choices,
    filters.device || "",
    (v) => set("device", v),
  );
}
