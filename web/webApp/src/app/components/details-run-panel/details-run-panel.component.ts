import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatTabsModule } from '@angular/material/tabs';
import { TPipe } from '../../i18n/i18n';
import { ActiveRunViewComponent } from './active-run-view.component';
import { MemberSelectionListComponent } from './member-selection-list.component';
import { RunHistoryListComponent } from './run-history-list.component';

@Component({
  selector: 'app-details-run-panel',
  standalone: true,
  imports: [
    MatTabsModule,
    TPipe,
    MemberSelectionListComponent,
    ActiveRunViewComponent,
    RunHistoryListComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './details-run-panel.component.html',
  styleUrl: './details-run-panel.component.css',
})
export class DetailsRunPanelComponent {}
