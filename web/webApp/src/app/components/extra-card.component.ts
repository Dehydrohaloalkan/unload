import { CommonModule, formatDate } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { TaskUiState } from '../app.models';
import { TPipe, t } from '../i18n/i18n';
import { UiConfirmService } from '../ui/ui-confirm.service';

@Component({
  selector: 'app-extra-card',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatCardModule, TPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './extra-card.component.html',
  styleUrls: ['./extra-card.component.css', './stage-button-state.css'],
})
export class ExtraCardComponent {
  readonly task = input.required<TaskUiState>();
  readonly canRun = input(false);
  readonly hasRunToday = input(false);
  readonly lastCompletedAt = input<string | null>(null);

  readonly start = output<void>();
  readonly openDetails = output<void>();

  private readonly confirmationService = inject(UiConfirmService);

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
      title: t('confirm.header'),
      acceptLabel: t('confirm.accept'),
      rejectLabel: t('confirm.reject'),
      onAccept: () => this.start.emit(),
    });
  }
}
