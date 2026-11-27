// Accès à l'API interne : tout passe par fetch sur la même origine (https://keystats.app/).
// Hors WebView2, la page tourne avec un serveur de fichiers et un dossier api/ d'exemple.

/** Échec d'une requête, avec le statut HTTP (0 si la requête n'a pas abouti). */
export class ApiError extends Error {
  constructor(path, status, detail) {
    super(
      status
        ? `${path} : HTTP ${status}${detail ? ` — ${detail}` : ""}`
        : `${path} : ${detail}`,
    );
    this.status = status;
  }
}

async function send(path, init) {
  let response;
  try {
    response = await fetch(path, init);
  } catch (error) {
    throw new ApiError(path, 0, error.message || "requête impossible");
  }
  if (!response.ok) {
    const detail = (await response.text().catch(() => "")).slice(0, 200);
    throw new ApiError(path, response.status, detail);
  }
  return response.json();
}

export const getJson = (path) => send(path);

export const postJson = (path, body) =>
  send(path, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
