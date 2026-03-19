import { CommonModule, formatDate } from '@angular/common';
import { Component, input } from '@angular/core';
import { Card } from 'primeng/card';
import { Tag } from 'primeng/tag';

@Component({
  selector: 'app-live-clock',
  standalone: true,
  imports: [CommonModule, Card, Tag],
  template: `
    <p-card styleClass="clock-card h-full">
      <ng-template #header>
        <div class="flex items-center justify-between px-6 pt-6">
          <div>
            <p class="text-xs font-medium uppercase tracking-[0.28em] text-slate-400">
              Серверное время
            </p>
            <h2 class="mt-2 text-2xl font-medium text-slate-800">Unload Control</h2>
          </div>
          <p-tag
            [severity]="connected() ? 'success' : 'warn'"
            [value]="connected() ? 'SignalR online' : 'SignalR offline'"
          />
        </div>
      </ng-template>

      <div class="flex flex-col gap-6">
        <div>
          <div class="clock-value">
            {{ formatTime(now()) }}
          </div>
          <div class="mt-3 text-base text-slate-500">
            {{ formatDateLabel(now()) }}
          </div>
        </div>
      </div>
    </p-card>
  `,
  styles: `
    :host {
      display: block;
    }

    .clock-value {
      font-size: clamp(3rem, 7vw, 5.5rem);
      line-height: 1;
      font-weight: 500;
      letter-spacing: -0.06em;
      color: #0f172a;
      font-variant-numeric: tabular-nums;
    }
  `,
})
export class LiveClockComponent {
  readonly now = input.required<Date>();
  readonly connected = input(false);

  formatTime(value: Date): string {
    return formatDate(value, 'HH:mm:ss', 'ru-RU');
  }

  formatDateLabel(value: Date): string {
    return formatDate(value, 'EEEE, d MMMM y', 'ru-RU');
  }
}
