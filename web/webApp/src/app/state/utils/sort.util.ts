export function sortCodes(codes: Iterable<string>): string[] {
  return Array.from(new Set(codes)).sort((left, right) => left.localeCompare(right));
}

export function sortNames(names: Iterable<string>): string[] {
  return Array.from(
    new Set(
      Array.from(names)
        .map((name) => name.trim())
        .filter((name) => name.length > 0),
    ),
  ).sort((left, right) => left.localeCompare(right));
}
