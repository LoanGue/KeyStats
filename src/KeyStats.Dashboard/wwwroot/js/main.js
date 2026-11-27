// Cadre du tableau de bord : routage par fragment (#/page) et en-tête.

import { defaultPage, pages } from "./pages.js";

const $ = (id) => document.getElementById(id);
const view = $("view");

function parseHash() {
  const [path] = location.hash.replace(/^#\/?/, "").split("?");
  return { id: Object.hasOwn(pages, path) ? path : defaultPage };
}

function renderNav(id) {
  for (const link of document.querySelectorAll(".nav a[data-page]")) {
    if (link.dataset.page === id) link.setAttribute("aria-current", "page");
    else link.removeAttribute("aria-current");
  }
}

async function render() {
  const { id } = parseHash();
  const page = pages[id];
  renderNav(id);
  document.title = `${page.title} · KeyStats`;
  $("page-title").textContent = page.title;
  $("page-description").textContent = page.description;
  view.replaceChildren();
  if (page.render) await page.render(view, {});
}

window.addEventListener("hashchange", render);
render();
