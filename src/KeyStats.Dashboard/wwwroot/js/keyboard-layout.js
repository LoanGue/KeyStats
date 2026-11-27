// Disposition AZERTY du portable et du pavé numérique : libellé, code de touche (KeyCode côté Core :
// scan code, +0x100 pour E0), largeur en touches, symbole de la case Maj.
// Fn n'a pas de code : le clavier la traite lui-même, Windows ne la reçoit jamais. Le bas de la touche
// Entrée (twin) est dessiné mais tu aux lecteurs d'écran, qui lisent déjà la touche sur la rangée du dessus.

/** [libellé, code, largeur, symbole Maj, texte affiché à la place du libellé]. */
const key = (label, code, width = 1, shifted = "", shown = label) => ({
  label,
  code,
  width,
  shifted,
  shown,
});

const letters = (labels, firstCode) =>
  labels.split(" ").map((l, i) => key(l, firstCode + i));

const functionRow = [
  { ...key("Échap", 0x01), name: "Échappement" },
  ...Array.from({ length: 10 }, (_, i) => key(`F${i + 1}`, 0x3b + i)),
  key("F11", 0x57),
  key("F12", 0x58),
  { ...key("Suppr", 0x153, 2), name: "Supprimer" },
];

const digitRow = [
  key("²", 0x29),
  ...["&", "é", '"', "'", "(", "-", "è", "_", "ç", "à"].map((l, i) =>
    key(l, 0x02 + i, 1, String((i + 1) % 10)),
  ),
  key(")", 0x0c, 1, "°"),
  key("=", 0x0d, 1, "+"),
  { ...key("Retour arr.", 0x0e, 2), name: "Retour arrière" },
];

/** Rangées du portable, de haut en bas, avec leur hauteur en pixels. */
export const rows = [
  { height: 30, keys: functionRow },
  { height: 44, keys: digitRow },
  {
    height: 44,
    keys: [
      key("Tab", 0x0f, 1.5),
      ...letters("a z e r t y u i o p ^ $", 0x10),
      key("Entrée", 0x1c, 1.5),
    ],
  },
  {
    height: 44,
    keys: [
      { ...key("Verr. Maj", 0x3a, 1.75), name: "Verrouillage majuscules" },
      ...letters("q s d f g h j k l m ù", 0x1e),
      key("*", 0x2b),
      // Bas de la touche Entrée en équerre : la même touche que sur la rangée du dessus.
      { ...key("Entrée", 0x1c, 1.25, "", "↵"), twin: true },
    ],
  },
  {
    height: 44,
    keys: [
      { ...key("Maj", 0x2a, 1.25), name: "Maj gauche" },
      key("<", 0x56),
      ...letters("w x c v b n , ; : !", 0x2c),
      { ...key("Maj", 0x36, 2.75), name: "Maj droite" },
    ],
  },
  {
    height: 44,
    keys: [
      { ...key("Ctrl", 0x1d, 1.25), name: "Ctrl gauche" },
      key("Fn", null),
      { ...key("Win", 0x15b), name: "Windows" },
      key("Alt", 0x38),
      key("Espace", 0x39, 5.75),
      key("AltGr", 0x138),
      { ...key("Ctrl", 0x11d), name: "Ctrl droite" },
      key("←", 0x14b, 0.75),
      key("↑", 0x148, 0.75),
      key("↓", 0x150, 0.75),
      key("→", 0x14d, 0.75),
    ],
  },
];

/** Pavé numérique, sur une grille de 4 colonnes et 5 rangées : [libellé, code, colonnes, rangées]. */
export const numpad = [
  ["Num", 0x45],
  ["/", 0x135],
  ["*", 0x37],
  ["−", 0x4a],
  ["7", 0x47],
  ["8", 0x48],
  ["9", 0x49],
  ["+", 0x4e, 1, 2],
  ["4", 0x4b],
  ["5", 0x4c],
  ["6", 0x4d],
  ["1", 0x4f],
  ["2", 0x50],
  ["3", 0x51],
  ["Entrée", 0x11c, 1, 2],
  ["0", 0x52, 2],
  [".", 0x53],
].map(([label, code, cols = 1, span = 1]) => ({
  ...key(label, code),
  cols,
  span,
  pad: true,
  ...(label === "Num" && { name: "Verrouillage numérique" }),
}));

/** Nom complet d'une touche, pour les lecteurs d'écran et le détail. */
export const keyName = (k) => k.name ?? (k.pad ? `Pavé ${k.label}` : k.label);

/** Touche de la carte qui porte code ; la moitié basse d'Entrée n'est pas retenue. */
export const keyOfCode = (code) =>
  [...rows.flatMap((r) => r.keys), ...numpad].find(
    (k) => k.code === code && !k.twin,
  );
