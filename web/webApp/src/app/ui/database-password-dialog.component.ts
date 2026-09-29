import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { DatabaseOptionResponse } from '../app.models';
import { TPipe, t } from '../i18n/i18n';
import { ApiClientService } from '../state/api-client.service';

@Component({
  selector: 'app-database-password-dialog',
  standalone: true,
  imports: [
    FormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    TPipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ 'database.dialogHeader' | t }}</h2>
    <mat-dialog-content class="database-dialog">
      <p>{{ 'database.dialogDescription' | t }}</p>
      <mat-form-field appearance="outline" subscriptSizing="dynamic">
        <mat-label>{{ 'database.databaseLabel' | t }}</mat-label>
        <mat-select
          id="database-option"
          [disabled]="submitting()"
          [ngModel]="databaseId()"
          (ngModelChange)="databaseId.set($event); error.set(null)"
        >
          @for (database of data.databases; track database.id) {
            <mat-option [value]="database.id">{{ database.name }}</mat-option>
          }
        </mat-select>
      </mat-form-field>
      <mat-form-field appearance="outline" subscriptSizing="dynamic">
        <mat-label>{{ 'database.usernameLabel' | t }}</mat-label>
        <input
          matInput
          cdkFocusInitial
          id="database-username"
          type="text"
          autocomplete="username"
          [disabled]="submitting()"
          [ngModel]="username()"
          (ngModelChange)="username.set($event); error.set(null)"
        />
      </mat-form-field>
      <mat-form-field appearance="outline" subscriptSizing="dynamic">
        <mat-label>{{ 'database.passwordLabel' | t }}</mat-label>
        <input
          matInput
          id="database-password"
          type="password"
          autocomplete="off"
          [disabled]="submitting()"
          [ngModel]="password()"
          (ngModelChange)="password.set($event); error.set(null)"
          (keydown.enter)="connect()"
        />
      </mat-form-field>
      @if (error(); as errorMessage) {
        <div class="app-message app-message--error" role="alert">{{ errorMessage }}</div>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button
        mat-flat-button
        type="button"
        [disabled]="
          databaseId().length === 0 ||
          username().trim().length === 0 ||
          password().length === 0 ||
          submitting()
        "
        (click)="connect()"
      >
        @if (submitting()) {
          <mat-spinner diameter="18" aria-label="Проверка подключения"></mat-spinner>
        } @else {
          {{ 'database.connect' | t }}
        }
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .database-dialog {
      display: grid;
      gap: 1rem;
      min-width: min(24rem, calc(100vw - 4rem));
      padding-top: 0.5rem !important;
    }

    .database-dialog p {
      margin: 0;
      color: #475569;
    }

    button mat-spinner {
      margin-inline: 1.5rem;
    }
  `,
})
export class DatabasePasswordDialogComponent {
  private readonly api = inject(ApiClientService);
  private readonly dialogRef = inject(MatDialogRef<DatabasePasswordDialogComponent, boolean>);
  readonly data = inject<DatabasePasswordDialogData>(MAT_DIALOG_DATA);

  readonly databaseId = signal(this.data.databases[0]?.id ?? '');
  readonly username = signal('');
  readonly password = signal('');
  readonly submitting = signal(false);
  readonly error = signal<string | null>(null);

  async connect(): Promise<void> {
    if (
      this.databaseId().length === 0 ||
      this.username().trim().length === 0 ||
      this.password().length === 0 ||
      this.submitting()
    ) {
      return;
    }

    this.submitting.set(true);
    this.error.set(null);
    try {
      await this.api.connectDatabase(this.databaseId(), this.username().trim(), this.password());
      this.password.set('');
      this.dialogRef.close(true);
    } catch {
      this.password.set('');
      this.error.set(t('database.connectionFailed'));
    } finally {
      this.submitting.set(false);
    }
  }
}

export interface DatabasePasswordDialogData {
  databases: DatabaseOptionResponse[];
}
