import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from 'primeng/button';
import { ConfirmDialog } from 'primeng/confirmdialog';
import { Dialog } from 'primeng/dialog';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { ProgressSpinner } from 'primeng/progressspinner';
import { AppErrorStore } from './app.error-store';
import { WorkflowStore } from './app.store';
import { TPipe, t } from './i18n/i18n';
import { byDescDate } from './state/utils/compare.util';
import { DetailsRunPanelComponent } from './components/details-run-panel/details-run-panel.component';
import { DetailsExtraPanelComponent } from './components/details-extra-panel/details-extra-panel.component';
import { DetailsTaskPanelComponent } from './components/details-task-panel.component';
import { DownloadHintToastComponent } from './components/download-hint-toast.component';
import { ExtraCardComponent } from './components/extra-card.component';
import { LiveClockComponent } from './components/live-clock.component';
import { PresetStageComponent } from './components/preset-stage.component';
import { RunCardComponent } from './components/run-card.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    Button,
    ConfirmDialog,
    Dialog,
    InputText,
    Message,
    ProgressSpinner,
    TPipe,
    DetailsRunPanelComponent,
    DetailsExtraPanelComponent,
    DetailsTaskPanelComponent,
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
  readonly appErrorStore = inject(AppErrorStore);

  readonly detailsPanelOpen = signal(false);
  readonly detailsPanelStage = signal<DrawerStage>('run');

  readonly adminDialogVisible = signal(false);
  readonly adminPassword = signal('');
  readonly adminError = signal<string | null>(null);

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
      this.adminDialogVisible.set(false);
      this.adminPassword.set('');
      this.adminError.set(null);
      return;
    }

    this.adminDialogVisible.set(true);
    this.adminPassword.set('');
    this.adminError.set(null);
  }

  confirmAdminMode(): void {
    const now = new Date();
    const expected = `${pad2(now.getHours())}${pad2(now.getMinutes())}`;
    if (this.adminPassword() !== expected) {
      this.adminError.set(t('app.admin.wrongPassword'));
      return;
    }

    this.store.setAdminMode(true);
    this.adminDialogVisible.set(false);
    this.adminPassword.set('');
    this.adminError.set(null);
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
}

type DrawerStage = 'run' | 'preset' | 'extra';

function pad2(value: number): string {
  return value.toString().padStart(2, '0');
}
