import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatTabsModule } from '@angular/material/tabs';
import { ExtraBankInfo } from '../../app.models';
import { WorkflowStore } from '../../app.store';
import { TPipe, t } from '../../i18n/i18n';
import { ActiveExtraViewComponent } from './active-extra-view.component';
import { RunHistoryListComponent } from '../details-run-panel/run-history-list.component';
import { UiConfirmService } from '../../ui/ui-confirm.service';

/**
 * Панель extra-выгрузки по образцу панели main run (вкладки «Выгрузка» / «История»).
 * Вкладка «Выгрузка»: выбор банков без скролла, выбрать все, тумблер шлюза, запуск, активный статус.
 * Вкладка «История»: переиспользует run-history-list с фильтром только extra (с отправкой в шлюз).
 */
@Component({
  selector: 'app-details-extra-panel',
  standalone: true,
  imports: [
    CommonModule,
    MatTabsModule,
    MatButtonModule,
    MatCheckboxModule,
    TPipe,
    ActiveExtraViewComponent,
    RunHistoryListComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './details-extra-panel.component.html',
  styleUrls: ['../details-run-panel/details-shared.css', './details-extra-panel.component.css'],
})
export class DetailsExtraPanelComponent implements OnInit {
  private readonly store = inject(WorkflowStore);
  private readonly confirmationService = inject(UiConfirmService);

  readonly banks = computed<ExtraBankInfo[]>(() => this.store.extraBanks());
  readonly banksLoading = computed<boolean>(() => this.store.extraBanksLoading());
  readonly publishToGateway = computed<boolean>(() => this.store.publishExtraToGateway());
  readonly canRun = computed<boolean>(() => this.store.canRunExtra());
  readonly hasRunToday = computed<boolean>(() => this.store.hasExtraToday());

  ngOnInit(): void {
    void this.store.loadExtraBanksAsync();
  }

  isBankSelected(code: string): boolean {
    return this.store.isExtraBankSelected(code);
  }

  allBanksSelected(): boolean {
    return this.store.allExtraBanksSelected();
  }

  /** Выбрана часть банков (не все и не пусто) — для indeterminate-состояния «Выбрать все». */
  banksPartial(): boolean {
    return this.store.someExtraBanksSelected();
  }

  toggleBank(code: string, checked: boolean): void {
    this.store.toggleExtraBank(code, checked);
  }

  toggleAllBanks(checked: boolean): void {
    if (checked) {
      this.store.selectAllExtraBanks();
    } else {
      this.store.deselectAllExtraBanks();
    }
  }

  setPublishToGateway(enabled: boolean): void {
    this.store.setPublishExtraToGateway(enabled);
  }

  handleRunClick(): void {
    if (!this.hasRunToday()) {
      void this.store.runExtraAsync();
      return;
    }

    this.confirmationService.confirm({
      message: t('confirm.runPrompt'),
      title: t('confirm.header'),
      acceptLabel: t('confirm.accept'),
      rejectLabel: t('confirm.reject'),
      onAccept: () => void this.store.runExtraAsync(),
    });
  }
}
