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
  const sentToGateway =
    batch?.status === SenderBatchStatus.Completed || (batch?.sentFiles?.length ?? 0) > 0;
  // Выгрузка без шлюза: партия помечается SkippedByRequest — мембер завершён успешно,
  // и без этого правила его рамка осталась бы жёлтой навсегда.
  const completedWithoutGateway =
    batch?.status === SenderBatchStatus.SkippedByRequest &&
    memberStatus?.status === MemberRunLifecycleStatus.Completed;
  const gatewayFailed = batch?.status === SenderBatchStatus.Failed;
  const memberFailed = memberStatus?.status === MemberRunLifecycleStatus.Failed;
  const memberCancelled = memberStatus?.status === MemberRunLifecycleStatus.Cancelled;

  if (memberCancelled && sentToGateway) {
    return 'member-card--border-green';
  }

  if ((sentToGateway || completedWithoutGateway) && !memberFailed) {
    return 'member-card--border-green';
  }

  if (memberFailed || gatewayFailed || memberCancelled) {
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
