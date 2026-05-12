export function readJson<T>(storage: Storage | null, key: string): T | null {
  if (!storage) {
    return null;
  }

  try {
    const raw = storage.getItem(key);
    return raw ? (JSON.parse(raw) as T) : null;
  } catch {
    return null;
  }
}

export function writeJson<T>(storage: Storage | null, key: string, value: T): void {
  if (!storage) {
    return;
  }

  try {
    storage.setItem(key, JSON.stringify(value));
  } catch {
    /* ignore quota or serialization errors */
  }
}

export function browserStorage(isBrowser: boolean): Storage | null {
  return isBrowser ? window.localStorage : null;
}
