import { Component } from '@angular/core';
import { TabsModule } from 'primeng/tabs';
import { ActiveRunViewComponent } from './active-run-view.component';
import { MemberSelectionListComponent } from './member-selection-list.component';
import { RunHistoryListComponent } from './run-history-list.component';

@Component({
  selector: 'app-details-run-panel',
  standalone: true,
  imports: [
    TabsModule,
    MemberSelectionListComponent,
    ActiveRunViewComponent,
    RunHistoryListComponent,
  ],
  templateUrl: './details-run-panel.component.html',
  styleUrl: './details-run-panel.component.css',
})
export class DetailsRunPanelComponent {}
