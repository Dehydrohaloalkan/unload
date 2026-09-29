import { TestBed } from '@angular/core/testing';
import { App } from './app';
import { AppErrorStore } from './app.error-store';
import { RU } from './i18n/ru';
import { ApiClientService } from './state/api-client.service';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        {
          provide: ApiClientService,
          useValue: {
            fetchDatabaseStatus: () =>
              Promise.resolve({
                configured: false,
                selectedDatabaseId: null,
                databases: [{ id: 'main', name: 'Основная' }],
              }),
            connectDatabase: () =>
              Promise.resolve({
                configured: true,
                selectedDatabaseId: 'main',
                databases: [{ id: 'main', name: 'Основная' }],
              }),
          },
        },
      ],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('should render dashboard title', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('h1')?.textContent).toContain(RU['app.title']);
  });

  it('should request the database password before loading workflow data', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();

    const dialog = document.body.querySelector('app-database-password-dialog');
    expect(dialog).not.toBeNull();
    expect(dialog!.textContent).toContain(RU['database.dialogHeader']);
  });

  it('should render the run details panel without an open action', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    fixture.detectChanges();

    const panel = fixture.nativeElement.querySelector('.details-panel') as HTMLElement | null;
    expect(panel).toBeTruthy();
    expect(panel?.textContent).toContain(RU['app.drawer.runTitle']);
  });

  it('should present an unhandled error in a prominent dialog', async () => {
    TestBed.inject(AppErrorStore).setUnhandledError(
      new Error('Не удалось прочитать файл конфигурации'),
    );
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await fixture.whenStable();

    const dialog = document.body.querySelector('[role="alertdialog"]');
    expect(dialog?.textContent).toContain(RU['errors.dialogUnexpectedTitle']);
    expect(dialog?.textContent).toContain('Не удалось прочитать файл конфигурации');
  });
});
