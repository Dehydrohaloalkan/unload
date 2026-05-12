import { CommonModule, formatDate } from '@angular/common';
import { Component, inject, input, output } from '@angular/core';
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
  readonly canRun = input(false);
  readonly hasRunToday = input(false);
  readonly lastCompletedAt = input<string | null>(null);

  readonly start = output<void>();
  readonly openDetails = output<void>();

  private readonly confirmationService = inject(ConfirmationService);

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
      accept: () => this.start.emit(),
    });
  }
}
