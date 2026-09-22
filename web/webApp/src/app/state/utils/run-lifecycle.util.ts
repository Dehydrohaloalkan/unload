import { RunStatusInfo } from '../../app.models';
import { isTerminalRunStatus } from './run-status.util';

export function isSameRunCorrelation(left: string | null | undefined, right: string | null | undefined): boolean {
  return Boolean(left && right && left.trim().toLowerCase() === right.trim().toLowerCase());
}

function parseTimestamp(value: string | null | undefined): number | null {
  if (!value) return null;
  const timestamp = Date.parse(value);
  return Number.isFinite(timestamp) ? timestamp : null;
}

/**
 * Accept a snapshot only if it belongs to the run currently being tracked and
 * cannot move that run backwards. SignalR and polling are independent streams,
 * so a late non-terminal snapshot must never undo an already observed terminal
 * state.
 */
export function shouldAcceptRunStatus(
  current: RunStatusInfo | null,
  next: RunStatusInfo,
  trackedCorrelationId: string | null,
): boolean {
  if (!isSameRunCorrelation(next.correlationId, trackedCorrelationId)) return false;
  if (current && !isSameRunCorrelation(current.correlationId, next.correlationId)) return false;

  if (current && isTerminalRunStatus(current.status) && !isTerminalRunStatus(next.status)) {
    return false;
  }

  const currentAt = parseTimestamp(current?.updatedAt);
  const nextAt = parseTimestamp(next.updatedAt);
  return currentAt === null || nextAt === null || nextAt >= currentAt;
}

export function newerTimestamp(
  current: string | null | undefined,
  candidate: string | null | undefined,
): string | null {
  if (!current) return candidate ?? null;
  if (!candidate) return current;
  const currentAt = parseTimestamp(current);
  const candidateAt = parseTimestamp(candidate);
  if (currentAt === null || candidateAt === null) return current;
  return candidateAt >= currentAt ? candidate : current;
}
