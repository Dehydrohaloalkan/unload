import { CommonModule, formatDate } from '@angular/common';
import { Component, input } from '@angular/core';
import { Tooltip } from 'primeng/tooltip';
import { Card } from 'primeng/card';

@Component({
  selector: 'app-live-clock',
  standalone: true,
  imports: [CommonModule, Card, Tooltip],
  templateUrl: './live-clock.component.html',
  styleUrl: './live-clock.component.css',
})
export class LiveClockComponent {
  readonly now = input.required<Date>();
  readonly connected = input(false);
  readonly probeCompleted = input(false);
  readonly completedAt = input<string | null>(null);
  readonly dayWindowSummary = input<string>('Ожидание дневного окна.');

  formatTime(value: Date): string {
    return formatDate(value, 'HH:mm:ss', 'ru-RU');
  }

  formatDateLabel(value: Date): string {
    return formatDate(value, 'EEEE, d MMMM y', 'ru-RU');
  }

  formatTimeFromIso(value: string): string {
    return formatDate(value, 'HH:mm:ss', 'ru-RU');
  }
}
