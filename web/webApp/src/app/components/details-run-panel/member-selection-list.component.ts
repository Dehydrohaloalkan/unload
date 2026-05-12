import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from 'primeng/button';
import { Checkbox } from 'primeng/checkbox';
import { MemberGroupViewModel, MemberViewModel } from '../../app.models';
import { WorkflowStore } from '../../app.store';
import { resolveMemberCardBorderClass } from '../../state/utils/member-card-style.util';
import { formatTimestamp, isTodayDate } from '../../state/utils/time.util';

@Component({
  selector: 'app-member-selection-list',
  standalone: true,
  imports: [CommonModule, FormsModule, Checkbox, Button],
  templateUrl: './member-selection-list.component.html',
  styleUrls: ['./details-shared.css', './member-selection-list.component.css'],
})
export class MemberSelectionListComponent {
  readonly store = inject(WorkflowStore);

  readonly selectedMemberKey = signal<string | null>(null);

  readonly memberSelectionStats = computed(() => {
    let total = 0;
    let selected = 0;
    for (const group of this.store.memberGroups()) {
      total += group.members.length;
      for (const member of group.members) {
        if (member.selected) {
          selected++;
        }
      }
    }
    return { total, selected };
  });

  readonly allMembersSelected = computed(() => {
    const { total, selected } = this.memberSelectionStats();
    return total > 0 && selected === total;
  });

  readonly allMembersPartial = computed(() => {
    const { total, selected } = this.memberSelectionStats();
    return total > 0 && selected > 0 && selected < total;
  });

  groupAllSelected(group: MemberGroupViewModel): boolean {
    return group.members.length > 0 && group.members.every((m) => m.selected);
  }

  groupPartial(group: MemberGroupViewModel): boolean {
    const selectedCount = group.members.filter((m) => m.selected).length;
    return selectedCount > 0 && selectedCount < group.members.length;
  }

  memberCardBorderClass(member: MemberViewModel): string {
    return resolveMemberCardBorderClass(member, this.latestTodayRun());
  }

  memberLastUploadToday(member: MemberViewModel): string | null {
    const run = this.latestTodayRun();
    if (!run) {
      return null;
    }

    const key = member.name.toLowerCase();
    const todays = (run.outputArtifacts ?? [])
      .filter(
        (a) =>
          a.occurredAt &&
          (a.memberName ?? '').toLowerCase() === key &&
          isTodayDate(a.occurredAt),
      )
      .sort(
        (left, right) =>
          new Date(right.occurredAt).getTime() - new Date(left.occurredAt).getTime(),
      );

    return todays[0]?.occurredAt ?? null;
  }

  toggleAll(selected: boolean): void {
    if (selected) {
      this.store.selectAllMembers();
    } else {
      this.store.clearMemberSelection();
    }
  }

  toggleGroup(group: MemberGroupViewModel, selected: boolean): void {
    for (const member of group.members) {
      this.store.toggleMember(member.targetCodes, selected);
    }
  }

  toggleMember(member: MemberViewModel, selected: boolean): void {
    this.store.toggleMember(member.targetCodes, selected);
  }

  selectMember(key: string): void {
    this.selectedMemberKey.update((current) => (current === key ? null : key));
  }

  setPublishToMq(checked: boolean): void {
    this.store.setPublishRunToMq(checked);
  }

  startSelected(): void {
    void this.store.startRunAsync();
  }

  formatTimestamp = formatTimestamp;

  private latestTodayRun() {
    const runs = this.store.todayRuns() ?? [];
    if (runs.length === 0) {
      return null;
    }
    return (
      [...runs].sort(
        (a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime(),
      )[0] ?? null
    );
  }
}
