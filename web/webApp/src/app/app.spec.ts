import { TestBed } from '@angular/core/testing';
import { App } from './app';
import { AppErrorStore } from './app.error-store';
import { RU } from './i18n/ru';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
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

  it('should present an unhandled error in a prominent dialog', async () => {
    TestBed.inject(AppErrorStore).setUnhandledError(new Error('Не удалось прочитать файл конфигурации'));
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await fixture.whenStable();

    const dialog = document.body.querySelector('[role="alertdialog"]');
    expect(dialog?.textContent).toContain(RU['errors.dialogUnexpectedTitle']);
    expect(dialog?.textContent).toContain('Не удалось прочитать файл конфигурации');
  });
});
