// Pages du tableau de bord : titre, description et filtres de l'en-tête, rendu de la vue.
// Une page sans render n'est pas encore livrée.
//
// render(view, ctx) remplit view, où l'état de chargement est déjà affiché. ctx porte filters,
// query (à ajouter aux routes de l'API), status, key, setFilters(next), retry() et unlock(), qui
// ouvre le dialogue de déverrouillage. Après chaque await, la vue vérifie ctx.isCurrent() avant
// d'écrire, car une autre page a pu être ouverte entre-temps.
// Une exception levée par render affiche l'état d'erreur avec « Réessayer ».

import { renderOverview } from "./overview.js";

export const pages = {
  ensemble: {
    title: "Vue d'ensemble",
    description: "L'essentiel de votre frappe sur la période choisie.",
    filters: true,
    render: renderOverview,
  },
  vitesse: {
    title: "Vitesse",
    description:
      "Vos sessions, leur dispersion et ce qui fait varier votre vitesse.",
    filters: true,
  },
  fautes: {
    title: "Fautes",
    description:
      "Où, comment et à quel prix vous vous trompez, et comment vous corrigez.",
    filters: true,
  },
  clavier: {
    title: "Clavier",
    description:
      "Chaque touche du portable, pavé numérique compris. Cliquez une touche pour son détail.",
    filters: true,
  },
  rythme: {
    title: "Rythme",
    description:
      "L'enchaînement des touches : ce qui vous ralentit, comment vous tenez les touches, quels doigts travaillent.",
    filters: true,
  },
  lettres: {
    title: "Lettres et mots",
    description: "Ce que vous écrivez, comparé à un français courant.",
    filters: true,
  },
  raccourcis: {
    title: "Raccourcis et combinaisons",
    description: "Les touches que vous tenez ensemble, volontairement ou non.",
    filters: true,
  },
  activite: {
    title: "Activité et origine",
    description: "Quand vous écrivez, et dans quelles applications.",
    filters: true,
  },
  detail: {
    title: "Détail à la milliseconde",
    description:
      "Une session rejouée touche par touche : appuis, relâchements, chevauchements.",
    filters: true,
  },
  reglages: {
    title: "Réglages",
    description:
      "Détection, compteur à l'écran, confidentialité et sauvegardes.",
    filters: false,
  },
};

export const defaultPage = "ensemble";
