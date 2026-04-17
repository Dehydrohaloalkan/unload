import { CommonModule, formatDate } from '@angular/common';
import { Component, computed, inject, input, output } from '@angular/core';
import { ConfirmationService } from 'primeng/api';
import { Button } from 'primeng/button';
import { Card } from 'primeng/card';
import { ConfirmDialog } from 'primeng/confirmdialog';
import { TaskUiState } from '../app.models';

@Component({
  selector: 'app-extra-card',
  standalone: true,
  imports: [CommonModule, Button, Card, ConfirmDialog],
  providers: [ConfirmationService],
  templateUrl: './extra-card.component.html',
  styleUrl: './extra-card.component.css',
})
export class ExtraCardComponent {
  readonly task = input.required<TaskUiState>();
  readonly now = input.required<Date>();
  readonly canRun = input(false);
  readonly hasRunToday = input(false);
  readonly lastCompletedAt = input<string | null>(null);
  readonly start = output<void>();
  readonly openDetails = output<void>();
  private readonly confirmationService = inject(ConfirmationService);

  readonly elapsedLabel = computed(() => {
    const startedAt = this.task().startedAt;
    if (!startedAt) {
      return '00:00';
    }

    const diffMs = Math.max(0, this.now().getTime() - new Date(startedAt).getTime());
    const totalSeconds = Math.floor(diffMs / 1000);
    const hours = Math.floor(totalSeconds / 3600);
    const minutes = Math.floor((totalSeconds % 3600) / 60);
    const seconds = totalSeconds % 60;

    if (hours > 0) {
      return `${pad(hours)}:${pad(minutes)}:${pad(seconds)}`;
    }

    return `${pad(minutes)}:${pad(seconds)}`;
  });

  statusLabel(): string {
    if (this.task().running) {
      return 'Running';
    }

    if (this.task().result) {
      return 'Completed';
    }

    if (this.task().error) {
      return 'Error';
    }

    return 'Готово';
  }

  statusSeverity(): 'success' | 'info' | 'warn' | 'danger' | 'secondary' {
    if (this.task().running) {
      return 'info';
    }

    if (this.task().result) {
      return 'success';
    }

    if (this.task().error) {
      return 'danger';
    }

    if (this.task().stale) {
      return 'warn';
    }

    return 'secondary';
  }

  completedLabel(): string {
    const value = this.lastCompletedAt();
    return value ? formatDate(value, 'HH:mm:ss', 'ru-RU') : '';
  }

  handleStartClick(): void {
    if (!this.hasRunToday()) {
      this.start.emit();
      return;
    }

    this.confirmationService.confirm({
      message: 'Точно запустить выгрузку?',
      header: 'Подтверждение',
      acceptLabel: 'Запустить',
      rejectLabel: 'Отмена',
      accept: () => {
        this.start.emit();
      },
    });
  }
}

function pad(value: number): string {
  return value.toString().padStart(2, '0');
}
