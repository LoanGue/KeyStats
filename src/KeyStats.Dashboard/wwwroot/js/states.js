// États communs d'une vue : chargement, période vide, agent arrêté, erreur.

const icons = {
  keyboard:
    '<rect x="2.5" y="6" width="19" height="12" rx="2"/><path d="M6.5 10h0M10 10h0M13.5 10h0M17 10h0M7.5 14h9"/>',
  pause: '<circle cx="12" cy="12" r="9"/><path d="M10 9v6M14 9v6"/>',
  alert: '<path d="M12 3l9.5 17h-19z"/><path d="M12 10v4M12 17.5h0"/>',
};

export function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

function button(label, primary, onClick) {
  const node = el("button", primary ? "btn btn-primary" : "btn", label);
  node.type = "button";
  node.addEventListener("click", onClick);
  return node;
}

function panel(view, { icon, bad, title, text, role }) {
  const box = el("section", "card state");
  if (role) box.setAttribute("role", role);
  const badge = el("span", bad ? "state-icon bad" : "state-icon");
  // Contenu fixe de ce module, jamais de données reçues.
  badge.innerHTML = `<svg viewBox="0 0 24 24" aria-hidden="true">${icons[icon]}</svg>`;
  box.append(badge, el("h2", null, title), el("p", null, text));
  view.replaceChildren(box);
  return box;
}

export function showLoading(view) {
  const skeleton = el("div", "skeleton");
  for (const width of [60, 50, 65]) {
    const card = el("div", "card");
    const top = el("span", "bar");
    top.style.width = `${width}%`;
    const value = el("span", "bar tall");
    value.style.width = `${width + 15}%`;
    card.append(top, value);
    skeleton.append(card);
  }
  const loading = el("div", "card loading");
  loading.setAttribute("role", "status");
  loading.append(
    el("span", "spinner"),
    el("span", null, "Chargement des statistiques…"),
  );
  view.replaceChildren(skeleton, loading);
}

/** Période sans frappe ; onAll passe à tout l'historique (absent quand la vue y est déjà). */
export function showEmpty(view, agentRunning, onAll) {
  const box = panel(view, {
    icon: "keyboard",
    title: "Pas encore de frappe sur cette période",
    text: agentRunning
      ? "Les statistiques apparaissent dès les premières minutes d'écriture. L'agent est actif et enregistre en arrière-plan."
      : "Les statistiques apparaissent dès les premières minutes d'écriture, une fois l'agent relancé.",
  });
  if (onAll) {
    const actions = el("div", "actions");
    actions.append(button("Afficher tout l'historique", false, onAll));
    box.append(actions);
  }
}

/** Période vide d'une page filtrée : propose tout l'historique, sauf quand la période y est déjà. */
export function showEmptyPeriod(view, ctx) {
  const all = ctx.filters.period === "all";
  showEmpty(
    view,
    ctx.status.agentRunning,
    all ? null : () => ctx.setFilters({ ...ctx.filters, period: "all" }),
  );
}

export function showStopped(view) {
  panel(view, {
    icon: "pause",
    bad: true,
    title: "L'agent est arrêté",
    text: "Rien n'est enregistré tant qu'il ne tourne pas. Ce qui a déjà été enregistré reste consultable ; le temps d'arrêt apparaîtra comme « non observé ».",
  });
}

export function showError(view, error, onRetry) {
  const message = String(error && error.message ? error.message : error);
  const box = panel(view, {
    icon: "alert",
    bad: true,
    role: "alert",
    title: "Impossible d'afficher cette vue",
    text: "La lecture des données a échoué. Les données enregistrées ne sont pas touchées.",
  });
  const actions = el("div", "actions");
  const copy = button("Copier le message", false, async () => {
    try {
      await navigator.clipboard.writeText(message);
      copy.textContent = "Message copié";
    } catch {
      copy.textContent = "Copie impossible";
    }
  });
  actions.append(button("Réessayer", true, onRetry), copy);
  box.append(el("code", null, message), actions);
}

/** Vue pas encore livrée dans cette version. */
export function showPending(view) {
  panel(view, {
    icon: "keyboard",
    title: "Vue pas encore disponible",
    text: "Cette page arrive dans une prochaine version. Les données sont déjà enregistrées et s'y afficheront.",
  });
}
