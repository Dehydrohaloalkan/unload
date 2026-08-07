import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  catalogGetCatalog$Json,
  catalogGetMembers$Json,
  gatewayRequeueRequeueToGateway$Json,
  runHistoryGetTodayRuns$Json,
  runHistoryGetWorkflowDashboard$Json,
  runLaunchGetExtraBanks$Json,
  runLaunchGetPresetState$Json,
  runLaunchRunExtra$Json,
  runLaunchRunPreset$Json,
  runLaunchStartRun$Json,
  runLaunchStopRun$Json,
  runStatusGetActiveRun$Json,
  runStatusGetRunByCorrelationId$Json,
  systemGetServerTime$Json,
  systemListOutputFiles$Json,
} from '../generated/api/functions';
import { UnloadApi } from '../generated/api/unload-api';
import {
  CatalogInfo,
  ExtraBankInfo,
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
import { ID_GENERATOR } from './id-generator.token';
import { joinApiUrl } from './utils/api-url.util';

@Injectable({ providedIn: 'root' })
export class ApiClientService {
  private readonly api = inject(UnloadApi);
  private readonly baseUrl = inject(API_BASE_URL);
  private readonly newId = inject(ID_GENERATOR);

  constructor() {
    this.api.rootUrl = this.baseUrl;
  }

  url(path: string): string {
    return joinApiUrl(this.baseUrl, path);
  }

  buildDownloadUrl = (path: string): string =>
    this.url(`/api/system/download?path=${encodeURIComponent(path)}`);

  buildArchiveUrl = (path: string): string =>
    this.url(`/api/system/download-archive?path=${encodeURIComponent(path)}`);

  fetchCatalog(): Promise<CatalogInfo> {
    return firstValueFrom(this.api.invoke(catalogGetCatalog$Json));
  }

  async fetchMembers(): Promise<MemberCatalogItem[]> {
    const items = await firstValueFrom(this.api.invoke(catalogGetMembers$Json));
    return items ?? [];
  }

  fetchPresetState(): Promise<PresetGateState> {
    return firstValueFrom(this.api.invoke(runLaunchGetPresetState$Json));
  }

  fetchServerTime(): Promise<ServerTimeResponse> {
    return firstValueFrom(this.api.invoke(systemGetServerTime$Json));
  }

  fetchDashboardSnapshot(): Promise<WorkflowDashboardSnapshotResponse> {
    return firstValueFrom(this.api.invoke(runHistoryGetWorkflowDashboard$Json));
  }

  async fetchTodayRuns(): Promise<RunStatusInfo[]> {
    const runs = await firstValueFrom(this.api.invoke(runHistoryGetTodayRuns$Json));
    return runs ?? [];
  }

  async fetchActiveRun(): Promise<RunStatusInfo | null> {
    try {
      const payload = await firstValueFrom(
        this.api.invoke(runStatusGetActiveRun$Json),
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
        this.api.invoke(runStatusGetRunByCorrelationId$Json, { correlationId }),
      );
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 404) {
        return null;
      }
      throw error;
    }
  }

  async fetchOutputFiles(path: string): Promise<OutputFileInfo[]> {
    const files = await firstValueFrom(this.api.invoke(systemListOutputFiles$Json, { path }));
    return files ?? [];
  }

  startRun(payload: {
    targetCodes: string[];
    memberCodes: string[];
    adminOverride: boolean;
    publishToGateway: boolean;
  }): Promise<RunAcceptedResponse> {
    return firstValueFrom(this.api.invoke(runLaunchStartRun$Json, { body: payload }));
  }

  stopRun(correlationId: string): Promise<unknown> {
    return firstValueFrom(this.api.invoke(runLaunchStopRun$Json, { correlationId }));
  }

  runPreset(adminOverride: boolean): Promise<ScriptTaskRunResult> {
    return firstValueFrom(
      this.api.invoke(runLaunchRunPreset$Json, { body: { adminOverride } }),
    );
  }

  async fetchExtraBanks(): Promise<ExtraBankInfo[]> {
    const banks = await firstValueFrom(this.api.invoke(runLaunchGetExtraBanks$Json));
    return banks ?? [];
  }

  runExtra(
    adminOverride: boolean,
    publishToGateway: boolean,
    selectedBanks: string[] | null,
  ): Promise<RunAcceptedResponse> {
    // Extra — deferred: API возвращает 202 Accepted + correlationId, статус трекается отдельно.
    return firstValueFrom(
      this.api.invoke(runLaunchRunExtra$Json, {
        body: { adminOverride, publishToGateway, selectedBanks },
      }),
    );
  }

  requeueToGateway(items: RequeueItem[]): Promise<RequeueToGatewayResponse> {
    return firstValueFrom(
      this.api.invoke(gatewayRequeueRequeueToGateway$Json, {
        body: { idempotencyKey: this.newId(), items, dryRun: false },
      }),
    );
  }
}
