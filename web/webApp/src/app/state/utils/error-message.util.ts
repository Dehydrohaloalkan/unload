import { HttpErrorResponse } from '@angular/common/http';
import { ProblemDetailsResponse } from '../../app.models';

export function toErrorMessage(error: unknown, fallback: string): string {
  if (error instanceof HttpErrorResponse) {
    if (typeof error.error === 'string' && error.error.trim()) {
      return error.error;
    }

    const details = error.error as ProblemDetailsResponse | null;
    if (details?.detail) {
      return details.detail;
    }
  }

  if (error instanceof Error && error.message.trim()) {
    return error.message;
  }

  return fallback;
}
