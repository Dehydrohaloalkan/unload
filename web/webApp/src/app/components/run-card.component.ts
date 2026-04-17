import { CommonModule, formatDate } from '@angular/common';
import { Component, inject, input, output, signal } from '@angular/core';
import { Button } from 'primeng/button';
import { Card } from 'primeng/card';
import { ConfirmDialog } from 'primeng/confirmdialog';
import { ConfirmationService } from 'primeng/api';
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
  imports: [CommonModule, Button, Card, ConfirmDialog],
  providers: [ConfirmationService],
  templateUrl: './run-card.component.html',
  styleUrl: './run-card.component.css',
})
export class RunCardComponent {
  readonly groups = input.required<MemberGroupViewModel[]>();
  readonly run = input<RunStatusInfo | null>(null);
  readonly selectedCount = input(0);
  readonly canStartRun = input(false);
  readonly runBusy = input(false);
  readonly hasRunToday = input(false);
  readonly lastCompletedAt = input<string | null>(null);
  readonly buildDownloadUrl = input.required<(path: string) => string>();
  readonly startRun = output<void>();
  readonly stopRun = output<void>();
  readonly openDetails = output<void>();
  readonly selectAll = output<void>();
  readonly clear = output<void>();
  readonly toggleMember = output<{ code: string; selected: boolean }>();

  readonly memberStatus = MemberRunLifecycleStatus;
  readonly expandedMemberCode = signal<string | null>(null);
  private readonly confirmationService = inject(ConfirmationService);

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

  handleStartRunClick(): void {
    if (!this.hasRunToday()) {
      this.startRun.emit();
      return;
    }

    this.confirmationService.confirm({
      message: 'Точно запустить выгрузку?',
      header: 'Подтверждение',
      acceptLabel: 'Запустить',
      rejectLabel: 'Отмена',
      accept: () => {
        this.startRun.emit();
      },
    });
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
