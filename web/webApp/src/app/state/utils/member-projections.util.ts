import {
  CatalogInfo,
  MemberCatalogItem,
  MemberGroupViewModel,
  MemberViewModel,
} from '../../app.models';
import { sortNames } from './sort.util';

export function buildMemberGroups(
  catalog: CatalogInfo | null,
  members: MemberCatalogItem[],
  selectedTargetCodes: string[],
): MemberGroupViewModel[] {
  if (!catalog) {
    return [];
  }

  const selected = new Set(selectedTargetCodes);
  const membersByCode = new Map(members.map((member) => [member.code, member]));

  return catalog.groups
    .map((group) => {
      const groupId = Number(group.id);
      const groupTargets = catalog.targets.filter((target) => Number(target.groupId) === groupId);
      const memberBuckets = new Map<string, typeof groupTargets>();

      for (const target of groupTargets) {
        const targetMemberName = (target.memberName ?? '').trim();
        const bucketKey = `${target.memberCode}::${targetMemberName.toLowerCase()}`;
        const bucket = memberBuckets.get(bucketKey);
        if (bucket) {
          bucket.push(target);
        } else {
          memberBuckets.set(bucketKey, [target]);
        }
      }

      const groupMembers = Array.from(memberBuckets.values())
        .map((memberTargets) => {
          if (memberTargets.length === 0) {
            return null;
          }

          const primaryTarget = memberTargets[0];
          const memberRecord = membersByCode.get(primaryTarget.memberCode) ?? null;
          const targetCodes = memberTargets
            .map((target) => target.targetCode)
            .sort((left, right) => left.localeCompare(right));
          const memberName =
            memberTargets[0].memberName?.trim() || memberRecord?.name || primaryTarget.memberCode;
          return {
            key: buildMemberKey(groupId, primaryTarget.memberCode, memberName),
            memberCode: primaryTarget.memberCode,
            memberFileExtension: primaryTarget.memberFileExtension?.trim() || null,
            name: memberName,
            targetCodes,
            selected: targetCodes.every((code) => selected.has(code)),
          } satisfies MemberViewModel;
        })
        .filter((member): member is MemberViewModel => !!member)
        .sort(
          (left, right) =>
            left.name.localeCompare(right.name) || left.memberCode.localeCompare(right.memberCode),
        );

      return {
        id: groupId,
        name: group.name,
        folder: group.folder,
        members: groupMembers,
      } satisfies MemberGroupViewModel;
    })
    .filter((group) => group.members.length > 0)
    .sort((left, right) => left.name.localeCompare(right.name));
}

export function buildHistoryMemberNames(
  catalog: CatalogInfo | null,
  members: MemberCatalogItem[],
): string[] {
  if (!catalog) {
    return sortNames(members.map((member) => member.name));
  }

  const names = new Set<string>();
  for (const target of catalog.targets) {
    const memberName = target.memberName?.trim();
    if (memberName) {
      names.add(memberName);
    }
  }

  for (const member of members) {
    const memberName = member.name?.trim();
    if (memberName) {
      names.add(memberName);
    }
  }

  return sortNames(names);
}

function buildMemberKey(groupId: number, memberCode: string, memberName: string): string {
  return `${groupId}:${memberCode}:${memberName.trim().toLowerCase()}`;
}
