import { CommonModule, formatDate } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { ConfirmationService } from 'primeng/api';
import { Button } from 'primeng/button';
import { Card } from 'primeng/card';
import { TaskUiState } from '../app.models';
import { TPipe, t } from '../i18n/i18n';

@Component({
  selector: 'app-extra-card',
  standalone: true,
  imports: [CommonModule, Button, Card, TPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
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
      message: t('confirm.runPrompt'),
      header: t('confirm.header'),
      acceptLabel: t('confirm.accept'),
      rejectLabel: t('confirm.reject'),
      accept: () => this.start.emit(),
    });
  }
}
