import {
  MemberRunLifecycleStatus,
  MemberViewModel,
  RunStatusInfo,
  SenderBatchStatus,
} from '../../app.models';
import { isTodayDate } from './time.util';

export type MemberCardBorder =
  | 'member-card--border-blue'
  | 'member-card--border-green'
  | 'member-card--border-yellow'
  | 'member-card--border-red';

export function resolveMemberCardBorderClass(
  member: MemberViewModel,
  latestRun: RunStatusInfo | null,
): MemberCardBorder {
  if (!latestRun) {
    return 'member-card--border-blue';
  }

  const memberStatus = findMemberRunStatus(latestRun, member.name);
  const hasFilesToday = hasMemberArtifactsToday(latestRun, member.name);

  if (!memberStatus && !hasFilesToday) {
    return 'member-card--border-blue';
  }

  const batch = findSenderBatchForMember(latestRun, member.name);
  const sentToMq =
    batch?.status === SenderBatchStatus.Completed || (batch?.sentFiles?.length ?? 0) > 0;
  const mqFailed = batch?.status === SenderBatchStatus.Failed;
  const memberFailed = memberStatus?.status === MemberRunLifecycleStatus.Failed;
  const memberCancelled = memberStatus?.status === MemberRunLifecycleStatus.Cancelled;

  if (memberCancelled && sentToMq) {
    return 'member-card--border-green';
  }

  if (sentToMq && !memberFailed) {
    return 'member-card--border-green';
  }

  if (memberFailed || mqFailed || memberCancelled) {
    return 'member-card--border-red';
  }

  return 'member-card--border-yellow';
}

function findMemberRunStatus(run: RunStatusInfo, memberName: string) {
  const key = memberName.toLowerCase();
  return (
    Object.values(run.memberStatuses ?? {}).find(
      (item) => item.memberName.toLowerCase() === key,
    ) ?? null
  );
}

function findSenderBatchForMember(run: RunStatusInfo, memberName: string) {
  const key = memberName.toLowerCase();
  return (
    Object.values(run.senderBatches ?? {}).find(
      (item) => item.memberName.toLowerCase() === key,
    ) ?? null
  );
}

function hasMemberArtifactsToday(run: RunStatusInfo, memberName: string): boolean {
  const key = memberName.toLowerCase();
  for (const artifact of run.outputArtifacts ?? []) {
    if (!artifact?.occurredAt) {
      continue;
    }
    if ((artifact.memberName ?? '').toLowerCase() !== key) {
      continue;
    }
    if (isTodayDate(artifact.occurredAt)) {
      return true;
    }
  }
  return false;
}
