import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AppErrorStore } from './app.error-store';
import { WorkflowStore } from './app.store';
import { TPipe, t } from './i18n/i18n';
import { byDescDate } from './state/utils/compare.util';
import { DetailsRunPanelComponent } from './components/details-run-panel/details-run-panel.component';
import { DetailsExtraPanelComponent } from './components/details-extra-panel/details-extra-panel.component';
import { DetailsTaskPanelComponent } from './components/details-task-panel.component';
import { CompletionConfettiComponent } from './components/completion-confetti.component';
import { DownloadHintToastComponent } from './components/download-hint-toast.component';
import { ExtraCardComponent } from './components/extra-card.component';
import { LiveClockComponent } from './components/live-clock.component';
import { PresetStageComponent } from './components/preset-stage.component';
import { RunCardComponent } from './components/run-card.component';
import { AdminLoginDialogComponent } from './ui/admin-login-dialog.component';
import { ErrorDialogComponent, ErrorDialogData } from './ui/error-dialog.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [
    CommonModule,
    MatButtonModule,
    MatProgressSpinnerModule,
    TPipe,
    DetailsRunPanelComponent,
    DetailsExtraPanelComponent,
    DetailsTaskPanelComponent,
    CompletionConfettiComponent,
    DownloadHintToastComponent,
    ExtraCardComponent,
    LiveClockComponent,
    PresetStageComponent,
    RunCardComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  readonly store = inject(WorkflowStore);
  private readonly appErrorStore = inject(AppErrorStore);
  private readonly dialog = inject(MatDialog);
  private errorDialogRef: MatDialogRef<ErrorDialogComponent> | null = null;
  private presentedErrorKey: string | null = null;

  readonly detailsPanelOpen = signal(false);
  readonly detailsPanelStage = signal<DrawerStage>('run');

  constructor() {
    effect(() => {
      const workflowMessage = this.store.errorMessage();
      const unhandledMessage = this.appErrorStore.unhandledErrorMessage();
      const source: ErrorSource | null = workflowMessage
        ? { kind: 'workflow', message: workflowMessage }
        : unhandledMessage
          ? { kind: 'unhandled', message: unhandledMessage }
          : null;

      if (!source) {
        this.presentedErrorKey = null;
        return;
      }

      const key = `${source.kind}:${source.message}`;
      if (key === this.presentedErrorKey) {
        return;
      }

      this.presentedErrorKey = key;
      this.presentError(source, key);
    });
  }

  readonly probeCompleted = computed(() => {
    const preset = this.store.presetState();
    return (preset?.readyForPreset ?? false) || (preset?.presetCompleted ?? false);
  });

  readonly stage2CompletedAt = computed(() => {
    const sorted = this.store
      .todayHistory()
      .filter((record) => record.taskCode === 'preset')
      .sort(byDescDate((record) => record.completedAt));
    return sorted[0]?.completedAt ?? null;
  });

  readonly dayWindowSummary = computed(() => {
    const preset = this.store.presetState();
    if (!preset) {
      return t('dayWindow.waiting');
    }
    if (preset.presetCompleted) {
      return t('dayWindow.active');
    }
    if (preset.readyForPreset) {
      return t('dayWindow.readyForPreset');
    }
    if (preset.pollingStarted) {
      return t('dayWindow.polling');
    }
    return t('dayWindow.notStarted');
  });

  toggleAdminMode(): void {
    if (this.store.adminMode()) {
      this.store.setAdminMode(false);
      return;
    }

    this.dialog
      .open(AdminLoginDialogComponent, {
        width: '26rem',
        maxWidth: 'calc(100vw - 2rem)',
        autoFocus: '#admin-password',
        restoreFocus: true,
      })
      .afterClosed()
      .subscribe((enabled) => {
        if (enabled) {
          this.store.setAdminMode(true);
        }
      });
  }

  openDetails(stage: DrawerStage): void {
    this.detailsPanelStage.set(stage);
    this.detailsPanelOpen.set(true);
  }

  closeDetails(): void {
    this.detailsPanelOpen.set(false);
  }

  startRunFromMainCard(): void {
    this.store.setPublishRunToGateway(true);
    // Главная карточка всегда запускает полную выгрузку, не трогая выбор мемберов в панели.
    void this.store.startRunAsync(true);
  }

  startExtraFromMainCard(): void {
    this.store.setPublishExtraToGateway(true);
    // Главная карточка всегда запускает полную выгрузку; подмножество банков — из панели деталей.
    void this.store.runExtraAsync(null);
  }

  private presentError(source: ErrorSource, key: string): void {
    if (this.presentedErrorKey !== key) {
      return;
    }

    this.errorDialogRef?.close();
    const data: ErrorDialogData = {
      title: source.kind === 'workflow' ? t('errors.dialogActionTitle') : t('errors.dialogUnexpectedTitle'),
      message: source.message,
      descriptionLabel: t('errors.dialogDescriptionLabel'),
      recoveryHint: t('errors.dialogRecoveryHint'),
      closeLabel: t('errors.dialogClose'),
    };

    const dialogRef = this.dialog.open(ErrorDialogComponent, {
      data,
      role: 'alertdialog',
      width: '42rem',
      maxWidth: 'calc(100vw - 1.5rem)',
      autoFocus: '[data-error-close]',
      restoreFocus: true,
      panelClass: 'app-error-dialog',
    });
    this.errorDialogRef = dialogRef;

    dialogRef.afterClosed().subscribe(() => {
      if (this.errorDialogRef === dialogRef) {
        this.errorDialogRef = null;
      }
      if (this.presentedErrorKey !== key) {
        return;
      }

      if (source.kind === 'workflow' && this.store.errorMessage() === source.message) {
        this.store.clearError();
      }
      if (
        source.kind === 'unhandled' &&
        this.appErrorStore.unhandledErrorMessage() === source.message
      ) {
        this.appErrorStore.clearUnhandledError();
      }
    });
  }
}

type DrawerStage = 'run' | 'preset' | 'extra';
type ErrorSource = { kind: 'workflow' | 'unhandled'; message: string };
