export function byDescDate<T>(get: (item: T) => string | null | undefined) {
  return (left: T, right: T): number => {
    const leftTime = toTime(get(left));
    const rightTime = toTime(get(right));
    return rightTime - leftTime;
  };
}

export function byAscDate<T>(get: (item: T) => string | null | undefined) {
  return (left: T, right: T): number => {
    const leftTime = toTime(get(left));
    const rightTime = toTime(get(right));
    return leftTime - rightTime;
  };
}

function toTime(value: string | null | undefined): number {
  if (!value) {
    return 0;
  }
  const time = new Date(value).getTime();
  return Number.isFinite(time) ? time : 0;
}
