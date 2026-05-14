import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { ProgressSpinner } from 'primeng/progressspinner';
import { AppErrorStore } from './app.error-store';
import { WorkflowStore } from './app.store';
import { DetailsRunPanelComponent } from './components/details-run-panel/details-run-panel.component';
import { DetailsTaskPanelComponent } from './components/details-task-panel.component';
import { DownloadHintToastComponent } from './components/download-hint-toast.component';
import { ExtraCardComponent } from './components/extra-card.component';
import { LiveClockComponent } from './components/live-clock.component';
import { PresetStageComponent } from './components/preset-stage.component';
import { RunCardComponent } from './components/run-card.component';

type DrawerStage = 'run' | 'preset' | 'extra';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    Button,
    Dialog,
    InputText,
    Message,
    ProgressSpinner,
    DetailsRunPanelComponent,
    DetailsTaskPanelComponent,
    DownloadHintToastComponent,
    ExtraCardComponent,
    LiveClockComponent,
    PresetStageComponent,
    RunCardComponent,
  ],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  readonly store = inject(WorkflowStore);
  readonly appErrorStore = inject(AppErrorStore);

  readonly detailsPanelOpen = signal(false);
  readonly detailsPanelStage = signal<DrawerStage>('run');

  adminDialogVisible = false;
  adminPassword = '';
  adminError: string | null = null;

  readonly probeCompleted = computed(() => {
    const preset = this.store.presetState();
    return (preset?.readyForPreset ?? false) || (preset?.presetCompleted ?? false);
  });

  readonly stage1CompletedAt = computed(() => {
    const task = this.store.presetTask();
    return task.result && task.completedAt ? task.completedAt : null;
  });

  readonly stage2CompletedAt = computed(() => {
    const sorted = this.store
      .todayHistory()
      .filter((record) => record.taskCode === 'preset')
      .sort(
        (left, right) =>
          new Date(right.completedAt).getTime() - new Date(left.completedAt).getTime(),
      );
    return sorted[0]?.completedAt ?? null;
  });

  readonly dayWindowSummary = computed(() => {
    const preset = this.store.presetState();
    if (!preset) {
      return 'Ожидание данных дневного окна.';
    }
    if (preset.presetCompleted) {
      return 'Дневное окно активно: preset выполнен, этапы 2-4 доступны.';
    }
    if (preset.readyForPreset) {
      return 'Проба успешна: можно запускать preset.';
    }
    if (preset.pollingStarted) {
      return 'Идет проверка проб.';
    }
    return 'Ожидается начало дневного окна.';
  });

  constructor() {
    this.store.init();
  }

  toggleAdminMode(): void {
    if (this.store.adminMode()) {
      this.store.setAdminMode(false);
      this.adminDialogVisible = false;
      this.adminPassword = '';
      this.adminError = null;
      return;
    }

    this.adminDialogVisible = true;
    this.adminPassword = '';
    this.adminError = null;
  }

  confirmAdminMode(): void {
    const now = new Date();
    const expected = `${pad2(now.getHours())}${pad2(now.getMinutes())}`;
    if (this.adminPassword !== expected) {
      this.adminError = 'Неверный пароль.';
      return;
    }

    this.store.setAdminMode(true);
    this.adminDialogVisible = false;
    this.adminPassword = '';
    this.adminError = null;
  }

  openDetails(stage: DrawerStage): void {
    this.detailsPanelStage.set(stage);
    this.detailsPanelOpen.set(true);
  }

  closeDetails(): void {
    this.detailsPanelOpen.set(false);
  }

  startRunFromMainCard(): void {
    this.store.selectAllMembers();
    this.store.setPublishRunToGateway(true);
    void this.store.startRunAsync();
  }

  startExtraFromMainCard(): void {
    this.store.setPublishExtraToGateway(true);
    void this.store.runExtraAsync();
  }
}

function pad2(value: number): string {
  return value.toString().padStart(2, '0');
}
