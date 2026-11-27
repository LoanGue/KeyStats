// Cartes de la page Fautes sur le texte tapé : précision par jour, paires confondues, accents.

import { baseOption, token, valueAxis } from "./charts.js";
import { dayLong, dayTick, nf, pct } from "./format.js";
import { el } from "./states.js";

const MIN_DAY = 200; // caractères de texte qu'il faut à un jour pour être « meilleur jour »
const MAX_PAIRS = 8; // lignes affichées par filtre de paires

const dateOf = (iso) => new Date(`${iso}T12:00:00`);

function figure(label, value) {
  const box = el("div");
  box.append(
    el("div", "typos-figure-label", label),
    el("div", "typos-figure-value", value == null ? "—" : pct(value)),
  );
  return box;
}

export function accuracyCard(data) {
  const card = {
    title: "Précision dans le temps",
    note: "Part des frappes qui ne sont pas effacées ensuite, jour par jour.",
    wide: true,
  };
  const days = data.days.filter((d) => d.accuracy != null);
  if (!days.length)
    return {
      ...card,
      empty:
        "Aucune session de texte sur cette période : la précision ne se mesure que sur le texte, pas sur le code.",
    };
  const best = days
    .filter((d) => d.typed >= MIN_DAY)
    .reduce((a, d) => (a && a.accuracy >= d.accuracy ? a : d), null);
  const aside = el("div", "typos-figures");
  aside.append(
    figure("Sur la période", data.accuracy),
    figure("Meilleur jour", best?.accuracy),
  );
  const round = (v) => (v == null ? null : Math.round(v * 1000) / 10);
  return {
    ...card,
    aside,
    label: `Précision par jour, ${pct(data.accuracy)} sur la période`,
    headers: ["Jour", "Caractères tapés", "Précision"],
    rows: days.map((d) => [
      dayLong.format(dateOf(d.day)),
      nf.format(d.typed),
      pct(d.accuracy),
    ]),
    option: {
      ...baseOption(),
      tooltip: {
        ...baseOption().tooltip,
        trigger: "axis",
        formatter: ([p]) => {
          const d = data.days[p.dataIndex];
          const value = d.accuracy == null ? "pas de texte" : pct(d.accuracy);
          return `<b>${dayLong.format(dateOf(d.day))}</b><br>${value}`;
        },
      },
      xAxis: {
        type: "category",
        boundaryGap: false,
        data: data.days.map((d) => d.day),
        axisLine: { lineStyle: { color: token("--border-strong") } },
        axisTick: { show: false },
        axisLabel: {
          color: token("--muted"),
          fontSize: 12,
          hideOverlap: true,
          formatter: (v) => dayTick.format(dateOf(v)),
        },
      },
      yAxis: valueAxis({
        name: "%",
        scale: true,
        max: 100,
        axisLabel: { formatter: (v) => nf.format(v) },
      }),
      series: [
        {
          type: "line",
          data: data.days.map((d) => round(d.accuracy)),
          showSymbol: false,
          lineStyle: { color: token("--chart"), width: 2 },
          itemStyle: { color: token("--chart") },
          areaStyle: { color: token("--chart-area"), opacity: 0.6 },
        },
      ],
    },
  };
}

const keyLabel = (c) => (c === " " ? "espace" : c);

export function key(c, typed) {
  return el("kbd", typed ? "typos-key typed" : "typos-key", keyLabel(c));
}

function pairRow(p) {
  const tr = el("tr");
  // Le flex vit dans un span : posé sur la cellule, il lui ôterait son rôle de cellule.
  const keys = el("span", "typos-fault");
  keys.append(
    key(p.typed, true),
    el("span", "typos-instead", "au lieu de"),
    key(p.wanted, false),
  );
  const fault = el("td");
  fault.append(keys);
  const kind = el("td");
  kind.append(
    el(
      "span",
      p.neighbors ? "typos-badge near" : "typos-badge far",
      p.neighbors ? "voisines" : "non voisines",
    ),
  );
  tr.append(fault, kind, el("td", "typos-count", nf.format(p.count)));
  return tr;
}

const pairFilters = [
  ["all", "Toutes", () => true],
  ["near", "Voisines", (p) => p.neighbors],
  ["far", "Non voisines", (p) => !p.neighbors],
];

export function pairsCard(pairs) {
  const card = {
    title: "Paires les plus confondues",
    note: "La touche tapée à la place de la bonne.",
  };
  if (!pairs.length)
    return {
      ...card,
      empty: "Aucune touche tapée à la place d'une autre sur cette période.",
    };
  const group = el("div", "seg small");
  group.setAttribute("role", "group");
  group.setAttribute("aria-label", "Filtrer les paires");
  const table = el("table", "typos-pairs");
  table.setAttribute("aria-label", "Paires confondues");
  const head = el("tr");
  head.append(...["Faute", "Touches", "Fois"].map((h) => el("th", null, h)));
  const body = el("tbody");
  const thead = el("thead");
  thead.append(head);
  table.append(thead, body);
  // Les boutons restent en place : le focus ne se perd pas au changement de filtre.
  const buttons = pairFilters.map(([id, label, keep]) => {
    const node = el("button", null, label);
    node.type = "button";
    node.addEventListener("click", () => show(id, keep));
    return node;
  });
  group.append(...buttons);
  const show = (id, keep) => {
    pairFilters.forEach(([v], i) =>
      buttons[i].setAttribute("aria-pressed", String(v === id)),
    );
    const rows = pairs.filter(keep).slice(0, MAX_PAIRS);
    body.replaceChildren(...rows.map(pairRow));
    if (!rows.length) {
      const td = el(
        "td",
        "typos-none",
        "Aucune paire de ce genre sur cette période.",
      );
      td.colSpan = 3;
      const tr = el("tr");
      tr.append(td);
      body.append(tr);
    }
  };
  show(pairFilters[0][0], pairFilters[0][2]);
  return { ...card, aside: group, body: table };
}

const LOWER = "éèêëàâùûüîïôçœ";

function accentTile(a, maxRate) {
  const rate = a.typed ? a.errors / a.typed : null;
  const tile = el("li", "typos-accent");
  const bar = el("span", "typos-accent-track");
  bar.setAttribute("aria-hidden", "true");
  const fill = el("span", "typos-accent-bar");
  fill.style.width = rate && maxRate ? `${(rate / maxRate) * 100}%` : "0";
  bar.append(fill);
  const detail = a.typed
    ? ` : ${nf.format(a.errors)} faute${a.errors > 1 ? "s" : ""} sur ${nf.format(a.typed)}`
    : " : jamais tapé sur cette période";
  tile.append(
    el("span", "typos-accent-char", a.character),
    el("span", "typos-accent-rate", rate == null ? "—" : pct(rate)),
    el("span", "sr-only", detail),
    bar,
  );
  return tile;
}

export function accentsCard(accents) {
  const card = {
    title: "Caractères accentués",
    note: "Taux de faute par caractère ; la barre s'allonge avec le taux.",
  };
  if (!accents.some((a) => a.typed))
    return {
      ...card,
      empty: "Aucun caractère accentué tapé en texte sur cette période.",
    };
  const maxRate = Math.max(
    ...accents.map((a) => (a.typed ? a.errors / a.typed : 0)),
  );
  const body = el("div", "typos-accents");
  for (const [id, title, list] of [
    [
      "typos-lower",
      "Minuscules",
      accents.filter((a) => LOWER.includes(a.character)),
    ],
    [
      "typos-upper",
      "Majuscules accentuées",
      accents.filter((a) => !LOWER.includes(a.character)),
    ],
  ]) {
    const heading = el("span", "typos-section", title);
    heading.id = id;
    const grid = el("ul", "typos-accent-grid");
    grid.setAttribute("aria-labelledby", id);
    grid.append(...list.map((a) => accentTile(a, maxRate)));
    body.append(heading, grid);
  }
  return { ...card, body };
}
