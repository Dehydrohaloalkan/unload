import { CommonModule, formatDate } from '@angular/common';
import { Component, inject, input, output } from '@angular/core';
import { ConfirmationService } from 'primeng/api';
import { Button } from 'primeng/button';
import { Card } from 'primeng/card';
import { ConfirmDialog } from 'primeng/confirmdialog';

@Component({
  selector: 'app-run-card',
  standalone: true,
  imports: [CommonModule, Button, Card, ConfirmDialog],
  providers: [ConfirmationService],
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
      message: 'Точно запустить выгрузку?',
      header: 'Подтверждение',
      acceptLabel: 'Запустить',
      rejectLabel: 'Отмена',
      accept: () => this.startRun.emit(),
    });
  }
}
