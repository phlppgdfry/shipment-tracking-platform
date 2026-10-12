let token = null, sessionVersion = 0;
export function authenticate(value) { token = value; sessionVersion++; }
export async function api(path, { method = 'GET', body, headers = {} } = {}) {
  const version = sessionVersion;
  const form = body instanceof FormData;
  const response = await fetch(path, { method, headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}),
    ...(body && !form ? { 'Content-Type': 'application/json' } : {}), ...headers }, body: body === undefined ? undefined : form ? body : JSON.stringify(body) });
  const text = await response.text();
  let result; try { result = text ? JSON.parse(text) : null; } catch { result = text; }
  if (version !== sessionVersion) throw new Error("Session changed. Previous response was discarded.");
  if (!response.ok) {
    const error = new Error(result?.title || result?.Title || (response.status === 401 ? 'Session expired. Please sign in again.' : `Request failed (${response.status}).`));
    error.status = response.status; throw error;
  }
  return { data: result, response };
}
export async function downloadDocument(id, name) {
  const version = sessionVersion;
  const response = await fetch(`/api/documents/${id}/download`, { headers: { Authorization: `Bearer ${token}` } });
  if (version !== sessionVersion) throw new Error("Session changed. Document download cancelled.");
  if (!response.ok) throw new Error(`Document download failed (${response.status}).`);
  const url = URL.createObjectURL(await response.blob());
  const link = document.createElement('a'); link.href = url; link.download = name; link.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
