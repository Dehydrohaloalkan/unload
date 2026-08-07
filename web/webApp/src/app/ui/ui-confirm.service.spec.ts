import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';
import { UiConfirmService } from './ui-confirm.service';

describe('UiConfirmService', () => {
  it('runs the action only after explicit acceptance', () => {
    const onAccept = vi.fn();
    const dialog = {
      open: vi.fn().mockReturnValue({ afterClosed: () => of(true) }),
    };

    TestBed.configureTestingModule({
      providers: [UiConfirmService, { provide: MatDialog, useValue: dialog }],
    });

    TestBed.inject(UiConfirmService).confirm({
      title: 'Подтверждение',
      message: 'Запустить повторно?',
      acceptLabel: 'Запустить',
      rejectLabel: 'Отмена',
      onAccept,
    });

    expect(dialog.open).toHaveBeenCalledOnce();
    expect(onAccept).toHaveBeenCalledOnce();
  });

  it('does not run the action after cancellation', () => {
    const onAccept = vi.fn();
    const dialog = {
      open: vi.fn().mockReturnValue({ afterClosed: () => of(false) }),
    };

    TestBed.configureTestingModule({
      providers: [UiConfirmService, { provide: MatDialog, useValue: dialog }],
    });

    TestBed.inject(UiConfirmService).confirm({
      title: 'Подтверждение',
      message: 'Запустить повторно?',
      acceptLabel: 'Запустить',
      rejectLabel: 'Отмена',
      onAccept,
    });

    expect(onAccept).not.toHaveBeenCalled();
  });
});
