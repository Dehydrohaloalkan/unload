import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  CatalogInfo,
  MemberCatalogItem,
  OutputFileInfo,
  PresetGateState,
  RequeueItem,
  RequeueToGatewayResponse,
  RunAcceptedResponse,
  RunStatusInfo,
  ScriptTaskRunResult,
  ServerTimeResponse,
  WorkflowDashboardSnapshotResponse,
} from '../app.models';
import { API_BASE_URL } from './api-base-url.token';
import { joinApiUrl } from './utils/api-url.util';

export interface ActiveRunPayload {
  correlationId: string | null;
  status?: RunStatusInfo['status'];
  createdAt?: string;
}

@Injectable({ providedIn: 'root' })
export class ApiClientService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  url(path: string): string {
    return joinApiUrl(this.baseUrl, path);
  }

  buildDownloadUrl = (path: string): string =>
    this.url(`/api/system/download?path=${encodeURIComponent(path)}`);

  buildArchiveUrl = (path: string): string =>
    this.url(`/api/system/download-archive?path=${encodeURIComponent(path)}`);

  fetchCatalog(): Promise<CatalogInfo> {
    return firstValueFrom(this.http.get<CatalogInfo>(this.url('/api/catalog')));
  }

  async fetchMembers(): Promise<MemberCatalogItem[]> {
    const items = await firstValueFrom(
      this.http.get<MemberCatalogItem[]>(this.url('/api/members')),
    );
    return items ?? [];
  }

  fetchPresetState(): Promise<PresetGateState> {
    return firstValueFrom(this.http.get<PresetGateState>(this.url('/api/runs/preset/state')));
  }

  fetchServerTime(): Promise<ServerTimeResponse> {
    return firstValueFrom(this.http.get<ServerTimeResponse>(this.url('/api/system/time')));
  }

  fetchDashboardSnapshot(): Promise<WorkflowDashboardSnapshotResponse> {
    return firstValueFrom(
      this.http.get<WorkflowDashboardSnapshotResponse>(this.url('/api/runs/dashboard')),
    );
  }

  async fetchTodayRuns(): Promise<RunStatusInfo[]> {
    const runs = await firstValueFrom(
      this.http.get<RunStatusInfo[]>(this.url('/api/runs/today')),
    );
    return runs ?? [];
  }

  async fetchActiveRun(): Promise<RunStatusInfo | ActiveRunPayload | null> {
    try {
      const payload = await firstValueFrom(
        this.http.get<RunStatusInfo | ActiveRunPayload>(this.url('/api/runs/active')),
      );
      return payload ?? null;
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 404) {
        return null;
      }
      throw error;
    }
  }

  async fetchRunStatus(correlationId: string): Promise<RunStatusInfo | null> {
    try {
      return await firstValueFrom(
        this.http.get<RunStatusInfo>(
          this.url(`/api/runs/${encodeURIComponent(correlationId)}`),
        ),
      );
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 404) {
        return null;
      }
      throw error;
    }
  }

  async fetchOutputFiles(path: string): Promise<OutputFileInfo[]> {
    const files = await firstValueFrom(
      this.http.get<OutputFileInfo[]>(
        this.url(`/api/system/output-files?path=${encodeURIComponent(path)}`),
      ),
    );
    return files ?? [];
  }

  startRun(payload: {
    targetCodes: string[];
    memberCodes: string[];
    adminOverride: boolean;
    publishToGateway: boolean;
  }): Promise<RunAcceptedResponse> {
    return firstValueFrom(this.http.post<RunAcceptedResponse>(this.url('/api/runs'), payload));
  }

  stopRun(correlationId: string): Promise<unknown> {
    return firstValueFrom(
      this.http.post(this.url(`/api/runs/${encodeURIComponent(correlationId)}/stop`), null),
    );
  }

  runPreset(adminOverride: boolean): Promise<ScriptTaskRunResult> {
    return firstValueFrom(
      this.http.post<ScriptTaskRunResult>(this.url('/api/runs/preset'), { adminOverride }),
    );
  }

  runExtra(adminOverride: boolean, publishToGateway: boolean): Promise<ScriptTaskRunResult> {
    return firstValueFrom(
      this.http.post<ScriptTaskRunResult>(this.url('/api/runs/extra'), {
        adminOverride,
        publishToGateway,
      }),
    );
  }

  requeueToGateway(items: RequeueItem[]): Promise<RequeueToGatewayResponse> {
    return firstValueFrom(
      this.http.post<RequeueToGatewayResponse>(this.url('/api/runs/requeue'), {
        idempotencyKey: crypto.randomUUID(),
        items,
        dryRun: false,
      }),
    );
  }
}
