import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from 'primeng/button';
import { Checkbox } from 'primeng/checkbox';
import { MemberGroupViewModel, MemberViewModel } from '../../app.models';
import { WorkflowStore } from '../../app.store';
import { byDescDate } from '../../state/utils/compare.util';
import { resolveMemberCardBorderClass } from '../../state/utils/member-card-style.util';
import { memberKey } from '../../state/utils/member-index.util';
import { formatTimestamp, isTodayDate } from '../../state/utils/time.util';

@Component({
  selector: 'app-member-selection-list',
  standalone: true,
  imports: [CommonModule, FormsModule, Checkbox, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
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
    return resolveMemberCardBorderClass(member, this.store.latestTodayRun());
  }

  memberLastUploadToday(member: MemberViewModel): string | null {
    const run = this.store.latestTodayRun();
    if (!run) {
      return null;
    }

    const key = memberKey(member.name);
    const todays = (run.outputArtifacts ?? [])
      .filter(
        (artifact) =>
          artifact.occurredAt &&
          memberKey(artifact.memberName) === key &&
          isTodayDate(artifact.occurredAt),
      )
      .sort(byDescDate((artifact) => artifact.occurredAt));

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

  setPublishToGateway(checked: boolean): void {
    this.store.setPublishRunToGateway(checked);
  }

  startSelected(): void {
    void this.store.startRunAsync();
  }

  formatTimestamp = formatTimestamp;
}
