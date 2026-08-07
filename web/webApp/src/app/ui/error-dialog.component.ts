import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';

export interface ErrorDialogData {
  title: string;
  message: string;
  descriptionLabel: string;
  recoveryHint: string;
  closeLabel: string;
}

@Component({
  selector: 'app-error-dialog',
  standalone: true,
  imports: [MatButtonModule, MatDialogModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="error-dialog__hero">
      <span class="error-dialog__icon" aria-hidden="true">
        <span class="app-icon app-icon--cancel"></span>
      </span>
      <div>
        <div class="error-dialog__eyebrow">ERROR</div>
        <h2 mat-dialog-title>{{ data.title }}</h2>
      </div>
    </div>

    <mat-dialog-content>
      <section class="error-dialog__details" aria-live="assertive">
        <div class="error-dialog__label">{{ data.descriptionLabel }}</div>
        <p>{{ data.message }}</p>
      </section>
      <p class="error-dialog__hint">{{ data.recoveryHint }}</p>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-flat-button type="button" data-error-close [mat-dialog-close]="true">
        {{ data.closeLabel }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    :host {
      display: block;
    }

    .error-dialog__hero {
      display: flex;
      align-items: center;
      gap: 1rem;
      padding: 1.5rem 1.5rem 0.75rem;
      background: linear-gradient(135deg, rgba(254, 226, 226, 0.92), rgba(255, 255, 255, 0));
    }

    .error-dialog__icon {
      display: grid;
      place-items: center;
      width: 3.25rem;
      height: 3.25rem;
      flex: 0 0 auto;
      color: #fff;
      border-radius: 1rem;
      background: linear-gradient(145deg, #dc2626, #b91c1c);
      box-shadow: 0 12px 28px rgba(185, 28, 28, 0.26);
    }

    .error-dialog__icon .app-icon {
      width: 1.55rem;
      height: 1.55rem;
    }

    .error-dialog__eyebrow {
      margin-bottom: 0.2rem;
      color: #b91c1c;
      font-size: 0.7rem;
      font-weight: 800;
      letter-spacing: 0.18em;
    }

    h2[mat-dialog-title] {
      margin: 0;
      padding: 0;
      color: #0f172a;
      font-size: clamp(1.35rem, 3vw, 1.85rem);
      line-height: 1.15;
      letter-spacing: -0.025em;
    }

    mat-dialog-content {
      display: grid;
      gap: 1rem;
      padding-top: 0.75rem !important;
    }

    .error-dialog__details {
      display: grid;
      gap: 0.45rem;
      padding: 1rem;
      border: 1px solid rgba(220, 38, 38, 0.22);
      border-radius: 0.9rem;
      background: rgba(254, 242, 242, 0.78);
    }

    .error-dialog__label {
      color: #991b1b;
      font-size: 0.75rem;
      font-weight: 800;
      letter-spacing: 0.08em;
      text-transform: uppercase;
    }

    .error-dialog__details p,
    .error-dialog__hint {
      margin: 0;
      line-height: 1.55;
      overflow-wrap: anywhere;
    }

    .error-dialog__details p {
      color: #450a0a;
      font-size: 1rem;
      font-weight: 550;
    }

    .error-dialog__hint {
      color: #475569;
      font-size: 0.875rem;
    }

    button[data-error-close] {
      min-width: 8rem;
      min-height: 44px;
    }

    @media (max-width: 480px) {
      .error-dialog__hero {
        align-items: flex-start;
        padding: 1.15rem 1rem 0.55rem;
      }

      .error-dialog__icon {
        width: 2.75rem;
        height: 2.75rem;
      }
    }
  `,
})
export class ErrorDialogComponent {
  readonly data = inject<ErrorDialogData>(MAT_DIALOG_DATA);
}
