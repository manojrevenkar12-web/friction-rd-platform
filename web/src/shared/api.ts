const KEYS = { lab: import.meta.env.VITE_LAB_KEY ?? "lab-key", engineer: import.meta.env.VITE_ENGINEER_KEY ?? "engineer-key" };

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message);
  }
}

export async function api<T>(path: string, init?: RequestInit & { as?: keyof typeof KEYS }): Promise<T> {
  let res: Response;
  try {
    res = await fetch(path, {
      ...init,
      headers: { "content-type": "application/json", "x-api-key": KEYS[init?.as ?? "lab"], ...(init?.headers ?? {}) },
    });
  } catch {
    throw new ApiError(0, "The platform API is not reachable. Start it with `dotnet run --project src/Rdo.Api` and reload.");
  }
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new ApiError(res.status, body.detail ?? body.title ?? `The API answered ${res.status}.`);
  }
  return res.json() as Promise<T>;
}

export async function staticJson<T>(file: string, missing: string): Promise<T> {
  const res = await fetch(`${import.meta.env.BASE_URL}data/${file}`);
  if (!res.ok) throw new Error(missing);
  return res.json() as Promise<T>;
}
