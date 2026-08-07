import { CommonModule, formatDate } from '@angular/common';
import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TPipe, t } from '../i18n/i18n';

@Component({
  selector: 'app-live-clock',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatTooltipModule, TPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './live-clock.component.html',
  styleUrl: './live-clock.component.css',
})
export class LiveClockComponent {
  readonly now = input.required<Date>();
  readonly connected = input(false);
  readonly probeCompleted = input(false);
  readonly dayWindowSummary = input<string>(t('dayWindow.default'));

  formatTime(value: Date): string {
    return formatDate(value, 'HH:mm:ss', 'ru-RU');
  }

  formatDateLabel(value: Date): string {
    return formatDate(value, 'EEEE, d MMMM y', 'ru-RU');
  }
}
