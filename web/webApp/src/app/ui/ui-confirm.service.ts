import { ChangeDetectionStrategy, Component, Injectable, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import {
  MAT_DIALOG_DATA,
  MatDialog,
  MatDialogModule,
  MatDialogRef,
} from '@angular/material/dialog';

export interface UiConfirmOptions {
  title: string;
  message: string;
  acceptLabel: string;
  rejectLabel: string;
  onAccept: () => void;
}

type UiConfirmDialogData = Omit<UiConfirmOptions, 'onAccept'>;

@Component({
  selector: 'app-ui-confirm-dialog',
  standalone: true,
  imports: [MatButtonModule, MatDialogModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content>{{ data.message }}</mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" [mat-dialog-close]="false">{{ data.rejectLabel }}</button>
      <button mat-flat-button type="button" color="primary" [mat-dialog-close]="true">
        {{ data.acceptLabel }}
      </button>
    </mat-dialog-actions>
  `,
})
export class UiConfirmDialogComponent {
  readonly data = inject<UiConfirmDialogData>(MAT_DIALOG_DATA);
}

@Injectable({ providedIn: 'root' })
export class UiConfirmService {
  private readonly dialog = inject(MatDialog);

  confirm(options: UiConfirmOptions): void {
    const { onAccept, ...data } = options;
    const dialogRef: MatDialogRef<UiConfirmDialogComponent, boolean> = this.dialog.open(
      UiConfirmDialogComponent,
      {
        data,
        width: '28rem',
        maxWidth: 'calc(100vw - 2rem)',
        autoFocus: 'first-tabbable',
        restoreFocus: true,
      },
    );

    dialogRef.afterClosed().subscribe((accepted) => {
      if (accepted) {
        onAccept();
      }
    });
  }
}
