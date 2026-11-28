// Cadre du tableau de bord : routage par fragment (#/page?filtres), en-tête et état de l'agent.

import { getJson } from "./api.js";
import {
  filterQuery,
  loadOptions,
  readFilters,
  renderFilters,
} from "./filters.js";
import { defaultPage, pages } from "./pages.js";
import { showError, showLoading, showPending } from "./states.js";
import { openSetup } from "./setup.js";
import { initUnlock } from "./unlock.js";

const $ = (id) => document.getElementById(id);
const view = $("view");
let status = { setup: false, agentRunning: false };
let key = null;
let renderId = 0;
// Après un déverrouillage ou un verrouillage, l'en-tête et la vue suivent la nouvelle clé.
const openUnlock = initUnlock(
  () => key,
  async () => {
    await refreshStatus();
    render();
  },
);

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
  const canUnlock = setup && !key.unlocked && (key.hello || key.passphrase);
  $("unlock-open").hidden = !canUnlock;
  $("key-lock").hidden = !setup || !key.unlocked;
  document.querySelector(".pill-sep").hidden =
    s.status !== "fulfilled" || !setup;
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
  const current = ++renderId;
  const { id, filters } = parseHash();
  const page = pages[id];
  // Une valeur ignorée disparaît aussi de l'adresse, sans entrée d'historique.
  const canonical = `#/${id}${filterQuery(filters)}`;
  if (location.hash !== canonical) history.replaceState(null, "", canonical);
  renderNav(id, filters);
  document.title = `${page.title} · KeyStats`;
  $("page-title").textContent = page.title;
  $("page-description").textContent = page.description;
  $("filters").hidden = !page.filters;
  if (page.filters)
    renderFilters($("filters"), filters, (next) => go(id, next));
  if (!page.render) {
    showPending(view);
    return;
  }
  showLoading(view);
  const ctx = {
    filters,
    query: filterQuery(filters),
    status,
    key,
    setFilters: (next) => go(id, next),
    retry: render,
    unlock: openUnlock,
    // Relit l'état de la clé pour l'en-tête, sans redessiner la vue.
    refreshKey: refreshStatus,
    // Une vue plus récente a pu être demandée pendant le chargement.
    isCurrent: () => current === renderId,
  };
  try {
    await page.render(view, ctx);
  } catch (error) {
    if (ctx.isCurrent()) showError(view, error, render);
  }
}

async function start() {
  await Promise.allSettled([refreshStatus(), loadOptions()]);
  window.addEventListener("hashchange", render);
  // L'agent peut être arrêté ou relancé pendant que la fenêtre est ouverte.
  // La vue affichée a reçu l'ancien état : elle est redessinée s'il a changé.
  window.addEventListener("focus", async () => {
    const before = status.agentRunning;
    await refreshStatus();
    if (status.agentRunning !== before) render();
  });
  // Premier lancement : l'assistant couvre la fenêtre jusqu'à ce que le chiffrement soit configuré.
  if (key && !key.setup) openSetup(() => refreshStatus().then(render));
  else render();
}

start();
