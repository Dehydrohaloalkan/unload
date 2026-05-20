import { CommonModule, formatDate } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { Button } from 'primeng/button';
import { Card } from 'primeng/card';
import { ProgressSpinner } from 'primeng/progressspinner';
import { ConfirmationService } from 'primeng/api';
import { PresetGateState, TaskUiState } from '../app.models';

@Component({
  selector: 'app-preset-stage',
  standalone: true,
  imports: [CommonModule, Button, Card, ProgressSpinner],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './preset-stage.component.html',
  styleUrl: './preset-stage.component.css',
})
export class PresetStageComponent {
  readonly presetState = input<PresetGateState | null>(null);
  readonly presetTask = input.required<TaskUiState>();
  readonly canRunPreset = input(false);
  readonly completedAt = input<string | null>(null);
  readonly startPreset = output<void>();
  readonly openDetails = output<void>();
  private readonly confirmationService = inject(ConfirmationService);

  readonly statusIconClass = computed(() => {
    const task = this.presetTask();
    if (task.running) {
      return 'app-icon app-icon--spinner app-icon--spin stage-icon stage-icon--spin';
    }
    if (this.completedAt()) {
      return 'app-icon app-icon--check-circle stage-icon stage-icon--success';
    }
    return 'app-icon app-icon--cancel stage-icon stage-icon--danger';
  });

  formatMoment(value: string | null | undefined): string {
    if (!value) {
      return '...';
    }
    return formatDate(value, 'HH:mm:ss', 'ru-RU');
  }

  onStartClick(): void {
    if (this.completedAt()) {
      this.confirmationService.confirm({
        message: 'Пресет уже выполнялся. Точно запустить повторно?',
        header: 'Подтверждение',
        acceptLabel: 'Запустить',
        rejectLabel: 'Отмена',
        accept: () => this.startPreset.emit(),
      });
      return;
    }

    this.startPreset.emit();
  }
}
