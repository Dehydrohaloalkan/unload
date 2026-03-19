import { CommonModule } from '@angular/common';
import { Component, computed, input, output } from '@angular/core';
import { Button } from 'primeng/button';
import { Card } from 'primeng/card';
import { Message } from 'primeng/message';
import { ProgressSpinner } from 'primeng/progressspinner';
import { Tag } from 'primeng/tag';
import { TaskUiState } from '../app.models';

@Component({
  selector: 'app-extra-card',
  standalone: true,
  imports: [CommonModule, Button, Card, Message, ProgressSpinner, Tag],
  template: `
    <p-card styleClass="task-card h-full">
      <ng-template #header>
        <div class="flex items-start justify-between gap-4 px-6 pt-6">
          <div>
            <p class="text-xs font-medium uppercase tracking-[0.28em] text-slate-400">
              Этап 2
            </p>
            <h2 class="mt-2 text-2xl font-medium text-slate-800">Extra-задача</h2>
          </div>
          <p-tag
            [severity]="statusSeverity()"
            [value]="statusLabel()"
          />
        </div>
      </ng-template>

      <div class="flex h-full flex-col gap-5">
        <div class="rounded-2xl border border-slate-100 bg-slate-50/80 p-4 text-sm text-slate-600">
          Пока карточка упрощена: один запуск, индикатор выполнения, таймер и итог выполнения через
          текущее API.
        </div>

        @if (task().running) {
          <div class="flex flex-col items-center gap-4 rounded-[1.75rem] border border-sky-100 bg-sky-50/80 px-5 py-8 text-center">
            <p-progress-spinner
              strokeWidth="4"
              [style]="{ width: '3rem', height: '3rem' }"
            />
            <div>
              <div class="text-lg font-medium text-sky-950">Extra выполняется</div>
              <div class="mt-1 text-sm text-sky-700">
                Прошло {{ elapsedLabel() }}
              </div>
            </div>
          </div>
        } @else {
          <p-button
            label="Запустить extra"
            icon="pi pi-bolt"
            size="large"
            [fluid]="true"
            [disabled]="!canRun()"
            (onClick)="start.emit()"
          />
        }

        @if (task().result; as result) {
          <div class="rounded-2xl border border-emerald-100 bg-emerald-50/80 p-4 text-sm text-emerald-900">
            <div class="font-medium">Последний запуск завершён</div>
            <div class="mt-2">Скриптов: {{ result.scriptsExecuted }}</div>
            <div>Файлов: {{ result.filesWritten }}</div>
            @if (result.outputPath) {
              <div class="break-all">Output: {{ result.outputPath }}</div>
            }
          </div>
        }

        @if (task().stale) {
          <p-message
            severity="warn"
            text="Страница была перезагружена во время extra-задачи. У текущего backend-контракта нет отдельного live-state для extra, поэтому точный статус после reload недоступен."
          />
        }

        @if (task().error; as error) {
          <p-message severity="error" [text]="error" />
        }
      </div>
    </p-card>
  `,
})
export class ExtraCardComponent {
  readonly task = input.required<TaskUiState>();
  readonly now = input.required<Date>();
  readonly canRun = input(false);
  readonly start = output<void>();

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
}

function pad(value: number): string {
  return value.toString().padStart(2, '0');
}
