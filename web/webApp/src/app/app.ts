import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { ProgressSpinner } from 'primeng/progressspinner';
import { ExtraCardComponent } from './components/extra-card.component';
import { DetailsExtraPanelComponent } from './components/details-extra-panel.component';
import { DetailsPresetPanelComponent } from './components/details-preset-panel.component';
import { DetailsRunPanelComponent } from './components/details-run-panel.component';
import { LiveClockComponent } from './components/live-clock.component';
import { PresetStageComponent } from './components/preset-stage.component';
import { RunCardComponent } from './components/run-card.component';
import { AppErrorStore } from './app.error-store';
import { MemberGroupViewModel, MemberViewModel, TaskRecord } from './app.models';
import { WorkflowStore } from './app.store';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    Button,
    Dialog,
    InputText,
    Message,
    ProgressSpinner,
    DetailsRunPanelComponent,
    DetailsPresetPanelComponent,
    DetailsExtraPanelComponent,
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
  readonly appErrorStore = inject(AppErrorStore);
  adminDialogVisible = false;
  adminPassword = '';
  adminError: string | null = null;
  detailsPanelOpen = false;
  detailsPanelStage: 'run' | 'preset' | 'extra' = 'run';
  drawerMemberCode = signal<string | null>(null);

  constructor() {
    this.store.init();
  }

  toggleAdminMode(): void {
    if (this.store.adminMode()) {
      this.store.setAdminMode(false);
      this.adminDialogVisible = false;
      this.adminPassword = '';
      this.adminError = null;
      return;
    }

    this.adminDialogVisible = true;
    this.adminPassword = '';
    this.adminError = null;
  }

  confirmAdminMode(): void {
    const now = new Date();
    const expected = `${now.getHours().toString().padStart(2, '0')}${now
      .getMinutes()
      .toString()
      .padStart(2, '0')}`;
    if (this.adminPassword !== expected) {
      this.adminError = 'Неверный пароль.';
      return;
    }

    this.store.setAdminMode(true);
    this.adminDialogVisible = false;
    this.adminPassword = '';
    this.adminError = null;
  }

  openDetails(stage: 'run' | 'preset' | 'extra'): void {
    this.detailsPanelStage = stage;
    this.detailsPanelOpen = true;
    this.drawerMemberCode.set(null);
  }

  closeDetails(): void {
    this.detailsPanelOpen = false;
  }

  detailRecords(taskCode: 'run' | 'preset' | 'extra'): TaskRecord[] {
    return this.store
      .todayHistory()
      .filter((record) => record.taskCode === taskCode)
      .sort(
        (left, right) =>
          new Date(right.completedAt).getTime() - new Date(left.completedAt).getTime(),
      );
  }

  formatTimestamp(value: string): string {
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? value : date.toLocaleString('ru-RU');
  }

  dayWindowSummary(): string {
    const preset = this.store.presetState();
    if (!preset) {
      return 'Ожидание данных дневного окна.';
    }

    if (preset.presetCompleted) {
      return 'Дневное окно активно: preset выполнен, этапы 2-4 доступны.';
    }

    if (preset.readyForPreset) {
      return 'Проба успешна: можно запускать preset.';
    }

    if (preset.pollingStarted) {
      return 'Идет проверка проб.';
    }

    return 'Ожидается начало дневного окна.';
  }

  stage1CompletedAt(): string | null {
    const task = this.store.presetTask();
    if (task.result && task.completedAt) {
      return task.completedAt;
    }

    return null;
  }

  stage1StatusClass(): string {
    return this.store.presetState()?.presetCompleted ? 'stage1-status--ok' : 'stage1-status--fail';
  }

  stage2CompletedAt(): string | null {
    const task = this.store.presetTask();
    if (task.result && task.completedAt) {
      return task.completedAt;
    }

    return null;
  }

  allMembersSelected(): boolean {
    const groups = this.store.memberGroups();
    const total = groups.reduce((sum, group) => sum + group.members.length, 0);
    return total > 0 && this.store.selectedCount() === total;
  }

  allMembersPartial(): boolean {
    const groups = this.store.memberGroups();
    const total = groups.reduce((sum, group) => sum + group.members.length, 0);
    const selected = this.store.selectedCount();
    return total > 0 && selected > 0 && selected < total;
  }

  setAllMembersSelection(selected: boolean): void {
    if (selected) {
      this.store.selectAllMembers();
      return;
    }

    this.store.clearMemberSelection();
  }

  groupAllSelected(group: MemberGroupViewModel): boolean {
    return group.members.length > 0 && group.members.every((member) => member.selected);
  }

  groupPartial(group: MemberGroupViewModel): boolean {
    const selectedCount = group.members.filter((member) => member.selected).length;
    return selectedCount > 0 && selectedCount < group.members.length;
  }

  setGroupSelection(group: MemberGroupViewModel, selected: boolean): void {
    for (const member of group.members) {
      this.store.toggleMember(member.code, selected);
    }
  }

  onToggleGroupFromDetails(groupId: number, selected: boolean): void {
    const group = this.store.memberGroups().find((item) => item.id === groupId);
    if (!group) {
      return;
    }

    this.setGroupSelection(group, selected);
  }

  selectDrawerMember(code: string): void {
    this.drawerMemberCode.set(code);
  }

  selectedDrawerMember(): MemberViewModel | null {
    const code = this.drawerMemberCode();
    if (!code) {
      return null;
    }

    for (const group of this.store.memberGroups()) {
      const member = group.members.find((item) => item.code === code);
      if (member) {
        return member;
      }
    }

    return null;
  }

  startSelectedRunFromDrawer(): void {
    void this.store.startRunAsync();
  }

  extraHistoryRecords(): TaskRecord[] {
    return this.detailRecords('extra');
  }

  presetHistoryRecords(): TaskRecord[] {
    return this.detailRecords('preset');
  }
}
