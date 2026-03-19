import { CommonModule, formatDate } from '@angular/common';
import { Component, input, output, signal } from '@angular/core';
import { Button } from 'primeng/button';
import { Card } from 'primeng/card';
import { Dialog } from 'primeng/dialog';
import { Panel } from 'primeng/panel';
import { Tag } from 'primeng/tag';
import {
  MemberGroupViewModel,
  MemberRunLifecycleStatus,
  RunOutputArtifactInfo,
  RunnerStep,
  RunLifecycleStatus,
  RunWorkerStatusInfo,
  RunStatusInfo,
} from '../app.models';

@Component({
  selector: 'app-run-card',
  standalone: true,
  imports: [CommonModule, Button, Card, Dialog, Panel, Tag],
  template: `
    <p-card styleClass="task-card h-full">
      <ng-template #header>
        <div class="flex items-start justify-between gap-4 px-6 pt-6">
          <div>
            <p class="text-xs font-medium uppercase tracking-[0.28em] text-slate-400">
              Этап 2
            </p>
            <h2 class="mt-2 text-2xl font-medium text-slate-800">Основной run</h2>
          </div>
          <p-tag
            [severity]="resolveRunSeverity()"
            [value]="resolveRunLabel()"
          />
        </div>
      </ng-template>

      <div class="flex flex-col gap-4">
        <div class="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-slate-100 bg-slate-50/80 p-4 text-sm text-slate-600">
          <div>
            Выбрано мемберов:
            <span class="font-semibold text-slate-800">{{ selectedCount() }}</span>
          </div>
          <div class="flex flex-wrap gap-2">
            <p-button
              label="Выбрать все"
              size="small"
              variant="text"
              (onClick)="selectAll.emit()"
            />
            <p-button
              label="Снять все"
              size="small"
              variant="text"
              (onClick)="clear.emit()"
            />
          </div>
        </div>

        @if (run(); as currentRun) {
          <div class="rounded-2xl border border-sky-100 bg-sky-50/80 p-4 text-sm text-sky-900">
            <div class="font-medium">Текущий correlationId</div>
            <div class="mt-1 break-all">{{ currentRun.correlationId }}</div>
            <div class="mt-1 text-sky-700">
              {{ currentRun.message || 'Выполнение продолжается.' }}
            </div>
            @if (currentRun.outputPath) {
              <div class="mt-2 text-sky-700">Результат: {{ currentRun.outputPath }}</div>
            }
            @if (resolveGlobalArtifacts(currentRun).length > 0) {
              <div class="mt-3 flex flex-wrap gap-2">
                @for (artifact of resolveGlobalArtifacts(currentRun); track artifact.filePath) {
                  <a
                    class="artifact-link"
                    [href]="buildDownloadUrl()(artifact.filePath)"
                    [attr.download]="artifact.fileName"
                  >
                    {{ artifact.fileName }}
                  </a>
                }
              </div>
            }
          </div>
        }

        <div class="flex flex-wrap gap-3">
          <p-button
            label="Запустить run"
            icon="pi pi-play"
            [loading]="runBusy()"
            [disabled]="!canStartRun()"
            (onClick)="startRun.emit()"
          />
          <p-button
            label="Остановить"
            severity="secondary"
            icon="pi pi-stop"
            [outlined]="true"
            [disabled]="!runBusy()"
            (onClick)="stopRun.emit()"
          />
        </div>

        @if (run(); as currentRun) {
          <div class="rounded-2xl border border-slate-100 bg-white/80 p-4">
            <div class="mb-3 flex items-center justify-between gap-3">
              <div class="font-medium text-slate-800">Потоки и скрипты</div>
              <div class="text-xs text-slate-400">console-like worker view</div>
            </div>

            @if (resolveWorkers(currentRun).length > 0) {
              <div
                class="worker-console"
                [style.gridTemplateColumns]="buildWorkerConsoleGrid(resolveWorkers(currentRun).length)"
              >
                @for (worker of resolveWorkers(currentRun); track worker.workerId) {
                  <div class="worker-console__column">
                    <div class="worker-console__head">Worker #{{ worker.workerId }}</div>
                    <div class="worker-console__cell">
                      {{ resolveWorkerConsoleLabel(worker) }}
                    </div>
                  </div>
                }
              </div>
            } @else {
              <div class="worker-console__empty">
                Пока нет live-статусов потоков.
              </div>
            }
          </div>
        }

        <div class="max-h-136 space-y-3 overflow-auto pr-1">
          @for (group of groups(); track group.id) {
            <p-panel [toggleable]="true" [collapsed]="false">
              <ng-template #header>
                <div class="flex items-center gap-3">
                  <span class="font-medium text-slate-800">{{ group.name }}</span>
                  <span class="text-xs uppercase tracking-[0.24em] text-slate-400">
                    {{ group.folder }}
                  </span>
                </div>
              </ng-template>

              <div class="member-grid">
                @for (member of group.members; track member.code) {
                  <button
                    type="button"
                    class="member-tile"
                    [class.member-tile--pending]="member.status === memberStatus.Pending"
                    [class.member-tile--running]="member.status === memberStatus.Running"
                    [class.member-tile--completed]="member.status === memberStatus.Completed"
                    [class.member-tile--failed]="member.status === memberStatus.Failed"
                    [class.member-tile--cancelled]="member.status === memberStatus.Cancelled"
                    [class.member-tile--selected]="member.selected"
                    [class.member-tile--expanded]="expandedMemberCode() === member.code"
                    (click)="toggleExpanded(member.code)"
                  >
                    <span class="member-tile__top">
                      <span class="member-tile__name">{{ member.name }}</span>
                      <span
                        class="member-tile__pick pi"
                        [class.pi-check-circle]="member.selected"
                        [class.pi-plus-circle]="!member.selected"
                        (click)="onToggleMember(member.code, member.selected, $event)"
                      ></span>
                    </span>
                    <span class="member-tile__bottom">
                      <span class="member-tile__status">{{ resolveMemberLabel(member.status) }}</span>
                      <span class="member-tile__meta">
                        {{ member.updatedAt ? formatMoment(member.updatedAt) : resolveStepLabel(member.lastStep) }}
                      </span>
                    </span>
                  </button>
                }
              </div>
            </p-panel>
          }
        </div>
      </div>
    </p-card>

    @if (resolveExpandedMember(); as member) {
      <p-dialog
        [visible]="true"
        [modal]="true"
        appendTo="body"
        [blockScroll]="true"
        [draggable]="false"
        [resizable]="false"
        [dismissableMask]="true"
        [breakpoints]="{ '1400px': '82vw', '960px': '92vw', '640px': '96vw' }"
        [style]="{ width: '68rem', maxWidth: '96vw' }"
        [contentStyle]="{
          paddingTop: '0.25rem',
          maxHeight: 'calc(100vh - 10rem)',
          overflow: 'auto'
        }"
        [header]="member.name"
        (onHide)="closeExpandedMember()"
      >
        <div class="member-details member-details--dialog">
          <div class="flex flex-wrap items-start justify-between gap-3">
            <div>
              <div class="flex flex-wrap items-center gap-2">
                <p-tag
                  [severity]="resolveMemberSeverity(member.status)"
                  [value]="resolveMemberLabel(member.status)"
                />
              </div>
              <div class="mt-3 text-sm text-slate-600">
                {{ member.message || 'Готов к запуску.' }}
              </div>
            </div>

            <div class="text-right text-xs text-slate-400">
              @if (member.lastStep !== null) {
                <div>{{ runnerStepLabel(member.lastStep) }}</div>
              }
              @if (member.updatedAt) {
                <div class="mt-1">{{ formatMoment(member.updatedAt) }}</div>
              }
            </div>
          </div>

          <div class="member-details__section">
            <div class="member-details__label">Target codes</div>
            <div class="member-details__chips">
              @for (targetCode of member.targetCodes; track targetCode) {
                <span class="member-details__chip">{{ targetCode }}</span>
              }
            </div>
          </div>

          <div class="member-details__section">
            <div class="member-details__label">Файлы</div>
            @if (member.outputArtifacts.length > 0) {
              <div class="artifact-list">
                @for (artifact of member.outputArtifacts; track artifact.filePath) {
                  <div class="artifact-item">
                    <a
                      class="artifact-link"
                      [href]="buildDownloadUrl()(artifact.filePath)"
                      [attr.download]="artifact.fileName"
                    >
                      {{ artifact.fileName }}
                    </a>
                    <div class="artifact-meta">{{ artifact.filePath }}</div>
                  </div>
                }
              </div>
            } @else {
              <div class="member-details__empty">Файлы ещё не созданы.</div>
            }
          </div>

          <div class="member-details__section">
            <div class="member-details__label">Логи</div>
            @if (member.logs.length > 0) {
              <div class="member-log-list">
                @for (line of member.logs; track line.time + '-' + line.step + '-' + line.message) {
                  <div class="member-log-row">
                    <span class="member-log-row__time">{{ formatMoment(line.time) }}</span>
                    <span class="member-log-row__step">{{ runnerStepLabel(line.step) }}</span>
                    <span class="member-log-row__message">{{ line.message }}</span>
                  </div>
                }
              </div>
            } @else {
              <div class="member-details__empty">Логи для этого мембера пока не пришли.</div>
            }
          </div>
        </div>
      </p-dialog>
    }
  `,
  styles: `
    :host {
      display: block;
    }

    .worker-console {
      display: inline-grid;
      gap: 0.75rem;
      overflow-x: auto;
      width: 100%;
      align-items: start;
    }

    .worker-console__column {
      display: grid;
      gap: 0.6rem;
      min-width: 12rem;
    }

    .worker-console__head,
    .worker-console__cell {
      border-radius: 0.95rem;
      padding: 0.8rem 0.9rem;
      font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, 'Liberation Mono', 'Courier New', monospace;
      font-size: 0.82rem;
      line-height: 1.4;
    }

    .worker-console__head {
      background: rgba(248, 250, 252, 0.7);
      color: #a16207;
      font-weight: 700;
      white-space: nowrap;
    }

    .worker-console__cell {
      background: rgba(248, 250, 252, 0.95);
      color: #334155;
      word-break: break-word;
      box-shadow: inset 0 0 0 1px rgba(226, 232, 240, 0.8);
    }

    .worker-console__empty {
      border-radius: 1rem;
      background: rgba(248, 250, 252, 0.9);
      padding: 1rem;
      color: #64748b;
      font-size: 0.875rem;
    }

    .member-grid {
      display: grid;
      grid-template-columns: repeat(6, minmax(0, 1fr));
      gap: 0.75rem;
    }

    .member-tile {
      display: flex;
      min-height: 5.5rem;
      flex-direction: column;
      justify-content: space-between;
      gap: 0.7rem;
      border: 1px solid rgba(226, 232, 240, 0.9);
      border-radius: 1rem;
      background: rgba(255, 255, 255, 0.86);
      padding: 0.75rem;
      text-align: left;
      cursor: pointer;
      transition:
        transform 140ms ease,
        box-shadow 140ms ease,
        border-color 140ms ease,
        background 140ms ease;
    }

    .member-tile:hover {
      transform: translateY(-1px);
      box-shadow: 0 10px 20px rgba(148, 163, 184, 0.12);
    }

    .member-tile--selected,
    .member-tile--expanded {
      box-shadow: inset 0 0 0 1px rgba(59, 130, 246, 0.28);
    }

    .member-tile--pending {
      color: #475569;
    }

    .member-tile--running {
      border-color: rgba(59, 130, 246, 0.45);
      background: rgba(239, 246, 255, 0.9);
      color: #1d4ed8;
    }

    .member-tile--completed {
      border-color: rgba(16, 185, 129, 0.35);
      background: rgba(236, 253, 245, 0.88);
      color: #047857;
    }

    .member-tile--failed {
      border-color: rgba(248, 113, 113, 0.4);
      background: rgba(254, 242, 242, 0.88);
      color: #b91c1c;
    }

    .member-tile--cancelled {
      border-color: rgba(251, 191, 36, 0.42);
      background: rgba(255, 251, 235, 0.88);
      color: #b45309;
    }

    .member-tile__top,
    .member-tile__bottom {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      flex-wrap: wrap;
      gap: 0.5rem;
    }

    .member-tile__name {
      display: -webkit-box;
      overflow: hidden;
      color: #0f172a;
      font-size: 0.87rem;
      font-weight: 600;
      line-clamp: 2;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
    }

    .member-tile__pick {
      flex: 0 0 auto;
      border-radius: 999px;
      color: #64748b;
      font-size: 1rem;
    }

    .member-tile__status,
    .member-tile__meta {
      font-size: 0.72rem;
      font-weight: 600;
    }

    .member-tile__status {
      min-width: 0;
      max-width: 100%;
      word-break: break-word;
    }

    .member-tile__meta {
      color: #64748b;
      font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, 'Liberation Mono', 'Courier New', monospace;
      white-space: nowrap;
    }

    .member-details {
      margin-top: 1rem;
      border-radius: 1.5rem;
      border: 1px solid rgba(226, 232, 240, 0.9);
      background: linear-gradient(180deg, rgba(255, 255, 255, 0.96), rgba(248, 250, 252, 0.96));
      padding: 1rem;
    }

    .member-details--dialog {
      margin-top: 0;
      border: none;
      background: transparent;
      padding: 0;
    }

    .member-details__section + .member-details__section {
      margin-top: 1rem;
    }

    .member-details__label {
      margin-bottom: 0.55rem;
      color: #64748b;
      font-size: 0.75rem;
      font-weight: 600;
      letter-spacing: 0.08em;
      text-transform: uppercase;
    }

    .member-details__chips {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
    }

    .member-details__chip {
      border-radius: 999px;
      background: rgba(255, 255, 255, 0.9);
      padding: 0.35rem 0.75rem;
      color: #475569;
      font-size: 0.75rem;
      box-shadow: 0 1px 2px rgba(15, 23, 42, 0.06);
    }

    .member-details__empty,
    .artifact-meta {
      color: #64748b;
      font-size: 0.85rem;
      word-break: break-all;
    }

    .artifact-list {
      display: grid;
      gap: 0.7rem;
    }

    .artifact-item {
      border-radius: 1rem;
      background: rgba(255, 255, 255, 0.86);
      padding: 0.8rem 0.95rem;
      box-shadow: inset 0 0 0 1px rgba(226, 232, 240, 0.8);
    }

    .artifact-link {
      color: #2563eb;
      font-weight: 600;
      text-decoration: none;
    }

    .artifact-link:hover {
      text-decoration: underline;
    }

    .member-log-list {
      display: grid;
      gap: 0.55rem;
    }

    .member-log-row {
      display: grid;
      grid-template-columns: 4.5rem 8rem minmax(0, 1fr);
      gap: 0.75rem;
      align-items: start;
      border-radius: 0.9rem;
      background: rgba(255, 255, 255, 0.86);
      padding: 0.7rem 0.85rem;
      color: #475569;
      font-size: 0.85rem;
    }

    .member-log-row__time,
    .member-log-row__step {
      color: #64748b;
      font-size: 0.78rem;
    }

    @media (max-width: 1700px) {
      .member-grid {
        grid-template-columns: repeat(5, minmax(0, 1fr));
      }
    }

    @media (max-width: 1450px) {
      .member-grid {
        grid-template-columns: repeat(4, minmax(0, 1fr));
      }
    }

    @media (max-width: 1180px) {
      .member-grid {
        grid-template-columns: repeat(3, minmax(0, 1fr));
      }
    }

    @media (max-width: 820px) {
      .member-grid {
        grid-template-columns: repeat(2, minmax(0, 1fr));
      }
    }
  `,
})
export class RunCardComponent {
  readonly groups = input.required<MemberGroupViewModel[]>();
  readonly run = input<RunStatusInfo | null>(null);
  readonly selectedCount = input(0);
  readonly canStartRun = input(false);
  readonly runBusy = input(false);
  readonly buildDownloadUrl = input.required<(path: string) => string>();
  readonly startRun = output<void>();
  readonly stopRun = output<void>();
  readonly selectAll = output<void>();
  readonly clear = output<void>();
  readonly toggleMember = output<{ code: string; selected: boolean }>();

  readonly memberStatus = MemberRunLifecycleStatus;
  readonly expandedMemberCode = signal<string | null>(null);

  resolveRunLabel(): string {
    const run = this.run();
    if (!run) {
      return 'Ожидание';
    }

    return this.resolveLifecycleLabel(run.status);
  }

  resolveRunSeverity(): 'success' | 'info' | 'warn' | 'danger' | 'secondary' {
    const run = this.run();
    if (!run) {
      return 'secondary';
    }

    switch (run.status) {
      case RunLifecycleStatus.Completed:
        return 'success';
      case RunLifecycleStatus.Failed:
        return 'danger';
      case RunLifecycleStatus.Cancelled:
      case RunLifecycleStatus.CancellationRequested:
        return 'warn';
      case RunLifecycleStatus.Running:
        return 'info';
      default:
        return 'secondary';
    }
  }

  resolveMemberSeverity(
    status: MemberRunLifecycleStatus,
  ): 'success' | 'info' | 'warn' | 'danger' | 'secondary' {
    switch (status) {
      case MemberRunLifecycleStatus.Completed:
        return 'success';
      case MemberRunLifecycleStatus.Running:
        return 'info';
      case MemberRunLifecycleStatus.Failed:
        return 'danger';
      case MemberRunLifecycleStatus.Cancelled:
        return 'warn';
      default:
        return 'secondary';
    }
  }

  resolveMemberLabel(status: MemberRunLifecycleStatus): string {
    switch (status) {
      case MemberRunLifecycleStatus.Completed:
        return 'Completed';
      case MemberRunLifecycleStatus.Running:
        return 'Running';
      case MemberRunLifecycleStatus.Failed:
        return 'Failed';
      case MemberRunLifecycleStatus.Cancelled:
        return 'Cancelled';
      default:
        return 'Pending';
    }
  }

  runnerStepLabel(step: RunnerStep): string {
    return RunnerStep[step];
  }

  resolveStepLabel(step: RunnerStep | null): string {
    return step === null ? '...' : this.runnerStepLabel(step);
  }

  formatMoment(value: string): string {
    return formatDate(value, 'HH:mm:ss', 'ru-RU');
  }

  toggleExpanded(code: string): void {
    this.expandedMemberCode.update((current) => (current === code ? null : code));
  }

  resolveExpandedMember(): MemberGroupViewModel['members'][number] | null {
    const expandedCode = this.expandedMemberCode();
    if (!expandedCode) {
      return null;
    }

    for (const group of this.groups()) {
      const member = group.members.find((item) => item.code === expandedCode);
      if (member) {
        return member;
      }
    }

    return null;
  }

  closeExpandedMember(): void {
    this.expandedMemberCode.set(null);
  }

  resolveWorkers(run: RunStatusInfo): RunWorkerStatusInfo[] {
    return Object.values(run.workerStatuses ?? {}).sort((left, right) => left.workerId - right.workerId);
  }

  buildWorkerConsoleGrid(workerCount: number): string {
    return `repeat(${Math.max(1, workerCount)}, minmax(12rem, 1fr))`;
  }

  resolveWorkerConsoleLabel(worker: RunWorkerStatusInfo): string {
    return worker.state === 'running'
      ? `running ${worker.scriptCode || 'unknown script'}`
      : 'idle';
  }

  resolveGlobalArtifacts(run: RunStatusInfo): RunOutputArtifactInfo[] {
    return (run.outputArtifacts ?? []).filter((artifact) => !artifact.memberName);
  }

  onToggleMember(code: string, selected: boolean, event: MouseEvent): void {
    event.stopPropagation();
    this.toggleMember.emit({ code, selected: !selected });
  }

  private resolveLifecycleLabel(status: RunLifecycleStatus): string {
    switch (status) {
      case RunLifecycleStatus.Completed:
        return 'Completed';
      case RunLifecycleStatus.Failed:
        return 'Failed';
      case RunLifecycleStatus.Cancelled:
        return 'Cancelled';
      case RunLifecycleStatus.CancellationRequested:
        return 'Stopping';
      case RunLifecycleStatus.Running:
        return 'Running';
      default:
        return 'Ожидание';
    }
  }
}
