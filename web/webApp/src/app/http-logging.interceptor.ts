import {
  HttpErrorResponse,
  HttpEvent,
  HttpEventType,
  HttpHandlerFn,
  HttpInterceptorFn,
  HttpRequest,
  HttpResponse,
} from '@angular/common/http';
import { isDevMode } from '@angular/core';
import { Observable, catchError, tap, throwError } from 'rxjs';

const MAX_BODY_CHARS = 2000;

function safeBodyPreview(body: unknown): unknown {
  if (body == null) {
    return body;
  }

  if (typeof body === 'string') {
    return body.length > MAX_BODY_CHARS ? `${body.slice(0, MAX_BODY_CHARS)}…` : body;
  }

  try {
    const json = JSON.stringify(body);
    if (json.length <= MAX_BODY_CHARS) {
      return body;
    }
    return `${json.slice(0, MAX_BODY_CHARS)}…`;
  } catch {
    return '[unserializable body]';
  }
}

function requestLabel(req: HttpRequest<unknown>): string {
  const url = req.urlWithParams ?? req.url;
  return `${req.method} ${url}`;
}

function shouldLog(): boolean {
  // Keep production console clean by default.
  return isDevMode();
}

export const httpLoggingInterceptor: HttpInterceptorFn = (
  req: HttpRequest<unknown>,
  next: HttpHandlerFn,
): Observable<HttpEvent<unknown>> => {
  if (!shouldLog()) {
    return next(req);
  }

  const startedAt = performance.now();
  const label = requestLabel(req);

  return next(req).pipe(
    tap((event) => {
      if (event.type !== HttpEventType.Response) {
        return;
      }

      const res = event as HttpResponse<unknown>;
      const elapsedMs = Math.round(performance.now() - startedAt);
      const bodyPreview = safeBodyPreview(res.body);

      // eslint-disable-next-line no-console
      console.log(`[api] ${label} -> ${res.status} (${elapsedMs}ms)`, bodyPreview);
    }),
    catchError((err: unknown) => {
      const elapsedMs = Math.round(performance.now() - startedAt);
      if (err instanceof HttpErrorResponse) {
        // eslint-disable-next-line no-console
        console.warn(
          `[api] ${label} -> ERROR ${err.status || 'n/a'} (${elapsedMs}ms)`,
          safeBodyPreview(err.error),
        );
      } else {
        // eslint-disable-next-line no-console
        console.warn(`[api] ${label} -> ERROR (${elapsedMs}ms)`, err);
      }

      return throwError(() => err);
    }),
  );
};

