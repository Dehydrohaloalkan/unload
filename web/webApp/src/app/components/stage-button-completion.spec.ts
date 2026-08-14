import { registerLocaleData } from '@angular/common';
import localeRu from '@angular/common/locales/ru';
import { TestBed } from '@angular/core/testing';
import { TaskUiState } from '../app.models';
import { ExtraCardComponent } from './extra-card.component';
import { PresetStageComponent } from './preset-stage.component';
import { RunCardComponent } from './run-card.component';

registerLocaleData(localeRu);

describe('completed stage buttons', () => {
  const idleTask: TaskUiState = {
    running: false,
    startedAt: null,
    completedAt: null,
    result: null,
    error: null,
    stale: false,
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ExtraCardComponent, PresetStageComponent, RunCardComponent],
    }).compileComponents();
  });

  it('dims the main run button after a run completed today', () => {
    const fixture = TestBed.createComponent(RunCardComponent);
    fixture.componentRef.setInput('canStartRun', true);
    fixture.componentRef.setInput('hasRunToday', true);
    fixture.detectChanges();

    expectCompletedButtonToBeDimmed(fixture.nativeElement);
  });

  it('dims the Extra button after an Extra run completed today', () => {
    const fixture = TestBed.createComponent(ExtraCardComponent);
    fixture.componentRef.setInput('task', idleTask);
    fixture.componentRef.setInput('canRun', true);
    fixture.componentRef.setInput('hasRunToday', true);
    fixture.detectChanges();

    expectCompletedButtonToBeDimmed(fixture.nativeElement);
  });

  it('dims the preset button after the preset completed', () => {
    const fixture = TestBed.createComponent(PresetStageComponent);
    fixture.componentRef.setInput('presetTask', idleTask);
    fixture.componentRef.setInput('canRunPreset', true);
    fixture.componentRef.setInput('completedAt', '2026-08-14T08:00:00Z');
    fixture.detectChanges();

    expectCompletedButtonToBeDimmed(fixture.nativeElement);
  });
});

function expectCompletedButtonToBeDimmed(host: HTMLElement): void {
  const button = host.querySelector<HTMLButtonElement>('.stage-action-button');

  document.body.appendChild(host);
  try {
    expect(button).not.toBeNull();
    expect(button?.classList.contains('stage-button--done')).toBe(true);
    expect(getComputedStyle(button!).opacity).toBe('0.62');
  } finally {
    host.remove();
  }
}
