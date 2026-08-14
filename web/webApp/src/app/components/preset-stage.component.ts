import { CommonModule, formatDate } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { PresetGateState, TaskUiState } from '../app.models';
import { TPipe, t } from '../i18n/i18n';
import { UiConfirmService } from '../ui/ui-confirm.service';

@Component({
  selector: 'app-preset-stage',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatCardModule, MatProgressSpinnerModule, TPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './preset-stage.component.html',
  styleUrls: ['./preset-stage.component.css', './stage-button-state.css'],
})
export class PresetStageComponent {
  readonly presetState = input<PresetGateState | null>(null);
  readonly presetTask = input.required<TaskUiState>();
  readonly canRunPreset = input(false);
  readonly completedAt = input<string | null>(null);
  readonly startPreset = output<void>();
  readonly openDetails = output<void>();
  private readonly confirmationService = inject(UiConfirmService);

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
        message: t('preset.confirmRerun'),
        title: t('confirm.header'),
        acceptLabel: t('confirm.accept'),
        rejectLabel: t('confirm.reject'),
        onAccept: () => this.startPreset.emit(),
      });
      return;
    }

    this.startPreset.emit();
  }
}
