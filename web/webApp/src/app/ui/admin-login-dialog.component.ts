import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TPipe, t } from '../i18n/i18n';

@Component({
  selector: 'app-admin-login-dialog',
  standalone: true,
  imports: [FormsModule, MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule, TPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ 'app.admin.dialogHeader' | t }}</h2>
    <mat-dialog-content class="admin-dialog">
      <mat-form-field appearance="outline" subscriptSizing="dynamic">
        <mat-label>{{ 'app.admin.passwordLabel' | t }}</mat-label>
        <input
          matInput
          cdkFocusInitial
          id="admin-password"
          type="password"
          inputmode="numeric"
          autocomplete="off"
          maxlength="4"
          [ngModel]="password()"
          (ngModelChange)="password.set($event); error.set(null)"
          (keydown.enter)="confirm()"
        />
      </mat-form-field>
      @if (error(); as errorMessage) {
        <div class="app-message app-message--error" role="alert">{{ errorMessage }}</div>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" [mat-dialog-close]="false">{{ 'confirm.reject' | t }}</button>
      <button mat-flat-button type="button" [disabled]="password().length !== 4" (click)="confirm()">
        <span class="app-icon app-icon--check" aria-hidden="true"></span>
        {{ 'app.admin.submit' | t }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .admin-dialog {
      display: grid;
      min-width: min(22rem, calc(100vw - 4rem));
      padding-top: 1.25rem !important;
    }
  `,
})
export class AdminLoginDialogComponent {
  private readonly dialogRef = inject(MatDialogRef<AdminLoginDialogComponent, boolean>);

  readonly password = signal('');
  readonly error = signal<string | null>(null);

  confirm(): void {
    if (this.password().length !== 4) {
      return;
    }

    const now = new Date();
    const expected = `${pad2(now.getHours())}${pad2(now.getMinutes())}`;
    if (this.password() !== expected) {
      this.error.set(t('app.admin.wrongPassword'));
      return;
    }

    this.dialogRef.close(true);
  }
}

function pad2(value: number): string {
  return value.toString().padStart(2, '0');
}
