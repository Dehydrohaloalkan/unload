import { CommonModule, formatDate } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { TPipe, t } from '../i18n/i18n';
import { UiConfirmService } from '../ui/ui-confirm.service';

@Component({
  selector: 'app-run-card',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatCardModule, TPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './run-card.component.html',
  styleUrls: ['./run-card.component.css', './stage-button-state.css'],
})
export class RunCardComponent {
  readonly canStartRun = input(false);
  readonly runBusy = input(false);
  readonly hasRunToday = input(false);
  readonly lastCompletedAt = input<string | null>(null);

  readonly startRun = output<void>();
  readonly openDetails = output<void>();

  private readonly confirmationService = inject(UiConfirmService);

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
      title: t('confirm.header'),
      acceptLabel: t('confirm.accept'),
      rejectLabel: t('confirm.reject'),
      onAccept: () => this.startRun.emit(),
    });
  }
}
