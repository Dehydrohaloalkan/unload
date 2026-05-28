import { CommonModule, formatDate } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { ConfirmationService } from 'primeng/api';
import { Button } from 'primeng/button';
import { Card } from 'primeng/card';
import { TPipe, t } from '../i18n/i18n';

@Component({
  selector: 'app-run-card',
  standalone: true,
  imports: [CommonModule, Button, Card, TPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './run-card.component.html',
  styleUrl: './run-card.component.css',
})
export class RunCardComponent {
  readonly canStartRun = input(false);
  readonly runBusy = input(false);
  readonly hasRunToday = input(false);
  readonly lastCompletedAt = input<string | null>(null);

  readonly startRun = output<void>();
  readonly openDetails = output<void>();

  private readonly confirmationService = inject(ConfirmationService);

  formatMoment(value: string): string {
    return formatDate(value, 'HH:mm:ss', 'ru-RU');
  }

  handleStartRunClick(): void {
    if (!this.hasRunToday()) {
      this.startRun.emit();
      return;
    }

    this.confirmationService.confirm({
      message: t('confirm.runPrompt'),
      header: t('confirm.header'),
      acceptLabel: t('confirm.accept'),
      rejectLabel: t('confirm.reject'),
      accept: () => this.startRun.emit(),
    });
  }
}
