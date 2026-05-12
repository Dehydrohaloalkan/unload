import {
  CatalogInfo,
  MemberCatalogItem,
  MemberGroupViewModel,
  MemberLogLine,
  MemberRunLifecycleStatus,
  MemberRunStatusInfo,
  MemberViewModel,
  RunnerEvent,
  RunnerStep,
  RunOutputArtifactInfo,
  RunStatusInfo,
} from '../../app.models';
import { sortCodes, sortNames } from './sort.util';

const MEMBER_LOG_LIMIT = 6;

export function buildMemberGroups(
  catalog: CatalogInfo | null,
  members: MemberCatalogItem[],
  activeRun: RunStatusInfo | null,
  events: RunnerEvent[],
  selectedTargetCodes: string[],
): MemberGroupViewModel[] {
  if (!catalog) {
    return [];
  }

  const selected = new Set(selectedTargetCodes);
  const membersByCode = new Map(members.map((member) => [member.code, member]));
  const memberLogs = buildMemberLogs(activeRun, events);
  const memberArtifacts = buildMemberArtifacts(activeRun?.outputArtifacts ?? []);
  const memberStatusesByName = new Map<string, MemberRunStatusInfo>(
    Object.values(activeRun?.memberStatuses ?? {}).map((status) => [
      status.memberName.toLowerCase(),
      status,
    ]),
  );

  return catalog.groups
    .map((group) => {
      const groupTargets = catalog.targets.filter((target) => target.groupId === group.id);
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
          const nameCandidates = sortCodes([
            ...memberTargets
              .map((target) => target.memberName?.trim() ?? '')
              .filter((name) => name.length > 0),
            memberRecord?.name ?? '',
          ]);
          const memberName =
            memberTargets[0].memberName?.trim() ||
            memberRecord?.name ||
            primaryTarget.memberCode;
          const status = resolveMemberStatus(
            memberRecord,
            findMemberRunStatus(memberStatusesByName, nameCandidates),
          );

          return {
            key: buildMemberKey(group.id, primaryTarget.memberCode, memberName),
            memberCode: primaryTarget.memberCode,
            memberFileExtension: primaryTarget.memberFileExtension?.trim() || null,
            name: memberName,
            targetCodes,
            selected: targetCodes.every((code) => selected.has(code)),
            status: status.status,
            lastStep: status.lastStep,
            message: status.message,
            updatedAt: status.updatedAt,
            logs: mergeMemberLogsByNames(memberLogs, nameCandidates),
            outputArtifacts: mergeMemberArtifactsByNames(memberArtifacts, nameCandidates),
          } satisfies MemberViewModel;
        })
        .filter((member): member is MemberViewModel => !!member)
        .sort(
          (left, right) =>
            left.name.localeCompare(right.name) ||
            left.memberCode.localeCompare(right.memberCode),
        );

      return {
        id: group.id,
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

function resolveMemberStatus(
  member: MemberCatalogItem | null,
  liveStatus: MemberRunStatusInfo | undefined,
): {
  status: MemberRunLifecycleStatus;
  lastStep: RunnerStep | null;
  message: string | null;
  updatedAt: string | null;
} {
  const status = liveStatus ?? member?.activeRunStatus;
  if (status) {
    return {
      status: status.status,
      lastStep: status.lastStep,
      message: status.message,
      updatedAt: status.updatedAt,
    };
  }

  return {
    status: MemberRunLifecycleStatus.Pending,
    lastStep: null,
    message: 'Готов к запуску.',
    updatedAt: null,
  };
}

function buildMemberLogs(
  activeRun: RunStatusInfo | null,
  events: RunnerEvent[],
): Map<string, MemberLogLine[]> {
  const result = new Map<string, MemberLogLine[]>();

  if (activeRun?.memberStatuses) {
    for (const member of Object.values(activeRun.memberStatuses)) {
      result.set(member.memberName.toLowerCase(), [
        {
          time: member.updatedAt,
          step: member.lastStep ?? RunnerStep.RequestAccepted,
          message: member.message ?? 'Статус обновлен.',
        },
      ]);
    }
  }

  for (const event of events) {
    if (!event.memberName) {
      continue;
    }

    const memberKey = event.memberName.toLowerCase();
    const current = result.get(memberKey) ?? [];
    const line: MemberLogLine = {
      time: event.occurredAt,
      step: event.step,
      message: event.message,
    };

    const isDuplicate = current.some(
      (item) =>
        item.time === line.time && item.step === line.step && item.message === line.message,
    );
    if (!isDuplicate) {
      current.unshift(line);
    }

    result.set(memberKey, current.slice(0, MEMBER_LOG_LIMIT));
  }

  return result;
}

function buildMemberArtifacts(
  artifacts: RunOutputArtifactInfo[],
): Map<string, RunOutputArtifactInfo[]> {
  const result = new Map<string, RunOutputArtifactInfo[]>();

  for (const artifact of artifacts) {
    if (!artifact.memberName) {
      continue;
    }

    const memberKey = artifact.memberName.toLowerCase();
    const current = result.get(memberKey) ?? [];
    current.push(artifact);
    current.sort(
      (left, right) => new Date(right.occurredAt).getTime() - new Date(left.occurredAt).getTime(),
    );
    result.set(memberKey, current);
  }

  return result;
}

function buildMemberKey(groupId: number, memberCode: string, memberName: string): string {
  return `${groupId}:${memberCode}:${memberName.trim().toLowerCase()}`;
}

function findMemberRunStatus(
  statusesByName: Map<string, MemberRunStatusInfo>,
  nameCandidates: string[],
): MemberRunStatusInfo | undefined {
  for (const candidate of nameCandidates) {
    const key = candidate.trim().toLowerCase();
    if (!key) {
      continue;
    }

    const status = statusesByName.get(key);
    if (status) {
      return status;
    }
  }

  return undefined;
}

function mergeMemberLogsByNames(
  logsByName: Map<string, MemberLogLine[]>,
  nameCandidates: string[],
): MemberLogLine[] {
  const unique = new Map<string, MemberLogLine>();
  for (const candidate of nameCandidates) {
    const key = candidate.trim().toLowerCase();
    if (!key) {
      continue;
    }

    for (const line of logsByName.get(key) ?? []) {
      unique.set(`${line.time}|${line.step}|${line.message}`, line);
    }
  }

  return Array.from(unique.values())
    .sort((left, right) => new Date(right.time).getTime() - new Date(left.time).getTime())
    .slice(0, MEMBER_LOG_LIMIT);
}

function mergeMemberArtifactsByNames(
  artifactsByName: Map<string, RunOutputArtifactInfo[]>,
  nameCandidates: string[],
): RunOutputArtifactInfo[] {
  const unique = new Map<string, RunOutputArtifactInfo>();
  for (const candidate of nameCandidates) {
    const key = candidate.trim().toLowerCase();
    if (!key) {
      continue;
    }

    for (const artifact of artifactsByName.get(key) ?? []) {
      unique.set(`${artifact.filePath}|${artifact.occurredAt}`, artifact);
    }
  }

  return Array.from(unique.values()).sort(
    (left, right) => new Date(right.occurredAt).getTime() - new Date(left.occurredAt).getTime(),
  );
}
