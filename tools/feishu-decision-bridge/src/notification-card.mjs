const REPORT_KINDS = new Set(['daily_report', 'weekly_report']);
const TASK_STATUSES = new Set([
  'completed',
  'pending_review',
  'blocked',
  'waiting_decision',
  'waiting_reply',
  'failed',
]);
const TASK_STATUS_LABELS = Object.freeze({
  completed: '已完成',
  pending_review: '待复审',
  blocked: '阻塞',
  waiting_decision: '待决定',
  waiting_reply: '待回复',
  failed: '失败',
});
const MAX_TITLE_CODE_POINTS = 120;
const MAX_REPORT_CODE_POINTS = 6000;
const MAX_TASK_FIELD_CODE_POINTS = 1000;
const MAX_TASK_ID_CODE_POINTS = 128;
const COMMIT_PATTERN = /^[0-9a-f]{7,40}$/;
const CONTROL_PATTERN = /[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/u;

function isPlainObject(value) {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    return false;
  }
  const prototype = Object.getPrototypeOf(value);
  return prototype === Object.prototype || prototype === null;
}

function snapshotExact(value, keys) {
  if (!isPlainObject(value) || Reflect.ownKeys(value).length !== keys.length) {
    return null;
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const snapshot = Object.create(null);
  for (const key of keys) {
    const descriptor = descriptors[key];
    if (!descriptor || !Object.hasOwn(descriptor, 'value') || !descriptor.enumerable) {
      return null;
    }
    snapshot[key] = descriptor.value;
  }
  return snapshot;
}

function validText(value, maximum, multiline = false) {
  return typeof value === 'string'
    && value.trim().length > 0
    && [...value].length <= maximum
    && !CONTROL_PATTERN.test(value)
    && (multiline || !/[\r\n]/u.test(value))
    && (multiline
      ? value.split(/\r?\n/u).every((line) => isSafeSingleLine(line))
      : isSafeSingleLine(value));
}

function plainText(content) {
  return {
    tag: 'div',
    text: {
      tag: 'plain_text',
      content,
    },
  };
}

function buildReportCard(notification) {
  const fields = snapshotExact(notification, ['kind', 'title', 'body']);
  if (
    fields === null
    || !REPORT_KINDS.has(fields.kind)
    || !validText(fields.title, MAX_TITLE_CODE_POINTS)
    || !validText(fields.body, MAX_REPORT_CODE_POINTS, true)
  ) {
    throw new Error('Invalid notification card input');
  }
  return {
    config: { wide_screen_mode: true },
    header: {
      template: fields.kind === 'daily_report' ? 'blue' : 'purple',
      title: { tag: 'plain_text', content: fields.title },
    },
    elements: [
      {
        tag: 'markdown',
        content: fields.body,
      },
    ],
  };
}

function buildTaskCard(notification) {
  const keys = [
    'kind',
    'taskId',
    'title',
    'status',
    'goal',
    'completed',
    'impact',
    'boundary',
    'verification',
    'next',
    'commitSha',
  ];
  const fields = snapshotExact(notification, keys);
  if (
    fields === null
    || fields.kind !== 'task_outcome'
    || !validText(fields.taskId, MAX_TASK_ID_CODE_POINTS)
    || !validText(fields.title, MAX_TITLE_CODE_POINTS)
    || !TASK_STATUSES.has(fields.status)
    || !validText(fields.goal, MAX_TASK_FIELD_CODE_POINTS)
    || !validText(fields.completed, MAX_TASK_FIELD_CODE_POINTS)
    || !validText(fields.impact, MAX_TASK_FIELD_CODE_POINTS)
    || !validText(fields.boundary, MAX_TASK_FIELD_CODE_POINTS)
    || !validText(fields.verification, MAX_TASK_FIELD_CODE_POINTS)
    || !validText(fields.next, MAX_TASK_FIELD_CODE_POINTS)
    || !(fields.commitSha === null || (
      typeof fields.commitSha === 'string' && COMMIT_PATTERN.test(fields.commitSha)
    ))
  ) {
    throw new Error('Invalid notification card input');
  }

  const footer = fields.commitSha === null
    ? '未形成已核验业务提交'
    : `提交：${fields.commitSha.slice(0, 12)}`;
  return {
    config: { wide_screen_mode: true },
    header: {
      template: fields.status === 'completed'
        ? 'green'
        : fields.status === 'pending_review'
          ? 'blue'
          : fields.status === 'failed'
            ? 'red'
            : 'orange',
      title: { tag: 'plain_text', content: `天章自动化 · ${TASK_STATUS_LABELS[fields.status]}` },
    },
    elements: [
      plainText(`任务：${fields.taskId} · ${fields.title}`),
      plainText(`1. 任务目标\n${fields.goal}`),
      plainText(`2. 本次完成\n${fields.completed}`),
      plainText(`3. 实际影响与明确边界\n影响：${fields.impact}\n边界：${fields.boundary}`),
      plainText(`4. 验证结果\n${fields.verification}`),
      plainText(`5. 后续关系\n${fields.next}`),
      {
        tag: 'note',
        elements: [{ tag: 'plain_text', content: footer }],
      },
    ],
  };
}

export function buildNotificationCard(notification) {
  const descriptor = isPlainObject(notification)
    ? Object.getOwnPropertyDescriptor(notification, 'kind')
    : null;
  if (
    descriptor
    && Object.hasOwn(descriptor, 'value')
    && descriptor.enumerable
    && REPORT_KINDS.has(descriptor.value)
  ) {
    return buildReportCard(notification);
  }
  return buildTaskCard(notification);
}
import { isSafeSingleLine } from './config.mjs';
