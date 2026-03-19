import { CommonModule, formatDate } from '@angular/common';
import { Component, input, output } from '@angular/core';
import { Button } from 'primeng/button';
import { Card } from 'primeng/card';
import { Message } from 'primeng/message';
import { ProgressSpinner } from 'primeng/progressspinner';
import { Tag } from 'primeng/tag';
import { PresetGateState, TaskUiState } from '../app.models';

@Component({
  selector: 'app-preset-stage',
  standalone: true,
  imports: [CommonModule, Button, Card, Message, ProgressSpinner, Tag],
  template: `
    <p-card styleClass="preset-card h-full">
      <ng-template #header>
        <div class="flex items-start justify-between gap-4 px-6 pt-6">
          <div>
            <p class="text-xs font-medium uppercase tracking-[0.28em] text-slate-400">
              Этап 1
            </p>
            <h2 class="mt-2 text-2xl font-medium text-slate-800">Проб и preset</h2>
          </div>
          <p-tag
            [severity]="resolveSeverity()"
            [value]="resolveStatusLabel()"
          />
        </div>
      </ng-template>

      <div class="flex flex-col gap-5">
        <p-message
          severity="secondary"
          [text]="presetState()?.message ?? 'Ожидаю состояние preset-гейта от API.'"
        />

        <div class="grid gap-3 rounded-2xl border border-slate-100 bg-slate-50/80 p-4 text-sm text-slate-600">
          <div class="flex items-center justify-between gap-4">
            <span>Мониторинг запущен</span>
            <span class="font-medium text-slate-700">
              {{ presetState()?.pollingStarted ? 'Да' : 'Нет' }}
            </span>
          </div>
          <div class="flex items-center justify-between gap-4">
            <span>Проб вернул</span>
            <span class="font-medium text-slate-700">
              {{ presetState()?.lastProbeValue ?? '...' }}
            </span>
          </div>
          <div class="flex items-center justify-between gap-4">
            <span>Последняя проверка</span>
            <span class="font-medium text-slate-700">
              {{ formatMoment(presetState()?.lastProbeAt) }}
            </span>
          </div>
          <div class="flex items-center justify-between gap-4">
            <span>Preset выполнен</span>
            <span class="font-medium text-slate-700">
              {{ presetState()?.presetCompleted ? 'Да' : 'Нет' }}
            </span>
          </div>
        </div>

        @if (presetTask().running) {
          <div class="flex items-center gap-3 rounded-2xl border border-sky-100 bg-sky-50/80 px-4 py-5 text-sky-900">
            <p-progress-spinner
              strokeWidth="4"
              [style]="{ width: '2rem', height: '2rem' }"
            />
            <div>
              <div class="font-medium">Выполняю preset-скрипт</div>
              <div class="text-sm text-sky-700">
                Скрипты выполняются через текущее API, после завершения откроется следующий экран.
              </div>
            </div>
          </div>
        } @else {
          <p-button
            label="Запустить preset"
            icon="pi pi-play"
            size="large"
            [fluid]="true"
            [disabled]="!canRunPreset()"
            (onClick)="startPreset.emit()"
          />
        }

        @if (presetTask().result; as result) {
          <div class="rounded-2xl border border-emerald-100 bg-emerald-50/80 p-4 text-sm text-emerald-900">
            <div class="font-medium">Preset завершён</div>
            <div class="mt-1">
              Скриптов: {{ result.scriptsExecuted }}, correlationId: {{ result.correlationId }}
            </div>
          </div>
        }

        @if (presetTask().error; as error) {
          <p-message severity="error" [text]="error" />
        }
      </div>
    </p-card>
  `,
})
export class PresetStageComponent {
  readonly presetState = input<PresetGateState | null>(null);
  readonly presetTask = input.required<TaskUiState>();
  readonly canRunPreset = input(false);
  readonly startPreset = output<void>();

  resolveStatusLabel(): string {
    const state = this.presetState();
    if (!state) {
      return 'Ожидание';
    }

    if (state.presetCompleted) {
      return 'Preset ok';
    }

    if (state.readyForPreset) {
      return 'Готово';
    }

    if (state.pollingStarted) {
      return 'Проб идёт';
    }

    return 'Ожидание окна';
  }

  resolveSeverity(): 'success' | 'info' | 'warn' | 'secondary' {
    const state = this.presetState();
    if (!state) {
      return 'secondary';
    }

    if (state.presetCompleted) {
      return 'success';
    }

    if (state.readyForPreset) {
      return 'info';
    }

    return 'warn';
  }

  formatMoment(value: string | null | undefined): string {
    if (!value) {
      return '...';
    }

    return formatDate(value, 'dd.MM.y HH:mm:ss', 'ru-RU');
  }
}
