import { CommonModule } from '@angular/common';
import { Component, computed, inject } from '@angular/core';
import { Message } from 'primeng/message';
import { ProgressSpinner } from 'primeng/progressspinner';
import { Tag } from 'primeng/tag';
import { ExtraCardComponent } from './components/extra-card.component';
import { LiveClockComponent } from './components/live-clock.component';
import { PresetStageComponent } from './components/preset-stage.component';
import { RunCardComponent } from './components/run-card.component';
import { WorkflowStore } from './app.store';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [
    CommonModule,
    Message,
    ProgressSpinner,
    Tag,
    ExtraCardComponent,
    LiveClockComponent,
    PresetStageComponent,
    RunCardComponent,
  ],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  readonly store = inject(WorkflowStore);
  readonly taskDeckVisible = computed(() => this.store.phase() === 'tasks');

  constructor() {
    this.store.init();
  }
}
