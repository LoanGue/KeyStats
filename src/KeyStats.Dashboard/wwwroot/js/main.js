// Cadre du tableau de bord : routage par fragment (#/page?filtres), en-tête et état de l'agent.

import { getJson } from "./api.js";
import {
  filterQuery,
  loadOptions,
  readFilters,
  renderFilters,
} from "./filters.js";
import { defaultPage, pages } from "./pages.js";

const $ = (id) => document.getElementById(id);
const view = $("view");
let status = { setup: false, agentRunning: false };
let key = null;

function parseHash() {
  const [path, query = ""] = location.hash.replace(/^#\/?/, "").split("?");
  const id = Object.hasOwn(pages, path) ? path : defaultPage;
  return { id, filters: readFilters(new URLSearchParams(query)) };
}

function go(id, filters) {
  location.hash = `#/${id}${filterQuery(filters)}`;
}

async function refreshStatus() {
  const [s, k] = await Promise.allSettled([
    getJson("/api/status"),
    getJson("/api/key"),
  ]);
  status = s.status === "fulfilled" ? s.value : status;
  key = k.status === "fulfilled" ? k.value : null;
  $("agent-on").hidden = s.status !== "fulfilled" || !status.agentRunning;
  $("agent-off").hidden = s.status !== "fulfilled" || status.agentRunning;
  const setup = key && key.setup;
  $("key-locked").hidden = !setup || key.unlocked;
  $("key-open").hidden = !setup || !key.unlocked;
}

function renderNav(id, filters) {
  for (const link of document.querySelectorAll(".nav a[data-page]")) {
    // Les liens de la navigation gardent les filtres choisis.
    link.href = `#/${link.dataset.page}${filterQuery(filters)}`;
    if (link.dataset.page === id) link.setAttribute("aria-current", "page");
    else link.removeAttribute("aria-current");
  }
}

async function render() {
  const { id, filters } = parseHash();
  const page = pages[id];
  renderNav(id, filters);
  document.title = `${page.title} · KeyStats`;
  $("page-title").textContent = page.title;
  $("page-description").textContent = page.description;
  $("filters").hidden = !page.filters;
  if (page.filters)
    renderFilters($("filters"), filters, (next) => go(id, next));
  view.replaceChildren();
  if (page.render)
    await page.render(view, {
      filters,
      query: filterQuery(filters),
      status,
      key,
      setFilters: (next) => go(id, next),
    });
}

async function start() {
  await Promise.allSettled([refreshStatus(), loadOptions()]);
  window.addEventListener("hashchange", render);
  // L'agent peut être arrêté ou relancé pendant que la fenêtre est ouverte.
  window.addEventListener("focus", refreshStatus);
  render();
}

start();
