import { isSafeSingleLine } from './config.mjs';
import { formatCustomReplyCommand } from './custom-reply.mjs';

const OPTION_KEYS = ['A', 'B', 'C'];
const IDENTIFIER_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$/;
const DECISION_ID_PATTERN = /^DEC-[0-9]{8}-[A-Z0-9]+$/;
const INVALID_FIELD = Symbol('invalid-field');
const MISSING_FIELD = Symbol('missing-field');

function isPlainObject(value) {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    return false;
  }
  const prototype = Object.getPrototypeOf(value);
  return prototype === Object.prototype || prototype === null;
}

function readDataField(descriptors, field, required = true, requireEnumerable = true) {
  const descriptor = descriptors[field];
  if (descriptor === undefined) {
    return required ? INVALID_FIELD : MISSING_FIELD;
  }
  if (
    !Object.hasOwn(descriptor, 'value')
    || (requireEnumerable && !descriptor.enumerable)
  ) {
    return INVALID_FIELD;
  }
  return descriptor.value;
}

function snapshotOption(value) {
  if (!isPlainObject(value)) {
    return null;
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const key = readDataField(descriptors, 'key');
  const label = readDataField(descriptors, 'label');
  if (key === INVALID_FIELD || label === INVALID_FIELD) {
    return null;
  }
  return { key, label };
}

function snapshotOptions(value) {
  if (!Array.isArray(value) || Object.getPrototypeOf(value) !== Array.prototype) {
    return null;
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const length = readDataField(descriptors, 'length', true, false);
  if (length !== OPTION_KEYS.length) {
    return null;
  }

  const options = [];
  for (let index = 0; index < OPTION_KEYS.length; index += 1) {
    const option = readDataField(descriptors, String(index));
    if (option === INVALID_FIELD) {
      return null;
    }
    const snapshot = snapshotOption(option);
    if (snapshot === null) {
      return null;
    }
    options.push(snapshot);
  }
  return options;
}

function snapshotDecision(value) {
  if (!isPlainObject(value)) {
    return null;
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const decisionId = readDataField(descriptors, 'decisionId');
  const taskId = readDataField(descriptors, 'taskId', false);
  const question = readDataField(descriptors, 'question');
  const optionsValue = readDataField(descriptors, 'options');
  const recommendedOption = readDataField(descriptors, 'recommendedOption');
  const impactSummary = readDataField(descriptors, 'impactSummary');
  if (
    decisionId === INVALID_FIELD
    || taskId === INVALID_FIELD
    || question === INVALID_FIELD
    || optionsValue === INVALID_FIELD
    || recommendedOption === INVALID_FIELD
    || impactSummary === INVALID_FIELD
  ) {
    return null;
  }
  const options = snapshotOptions(optionsValue);
  if (options === null) {
    return null;
  }
  return {
    decisionId,
    taskId: taskId === MISSING_FIELD ? undefined : taskId,
    taskIdProvided: taskId !== MISSING_FIELD,
    question,
    options,
    recommendedOption,
    impactSummary,
  };
}

function isIdentifier(value) {
  return typeof value === 'string' && IDENTIFIER_PATTERN.test(value);
}

function isSafeDisplayText(value) {
  return typeof value === 'string'
    && value.trim().length > 0
    && isSafeSingleLine(value);
}

function validateInput(decision, cardNonce) {
  if (
    decision === null
    || typeof decision.decisionId !== 'string'
    || !DECISION_ID_PATTERN.test(decision.decisionId)
    || (decision.taskIdProvided && !isIdentifier(decision.taskId))
    || !isSafeDisplayText(decision.question)
    || !OPTION_KEYS.includes(decision.recommendedOption)
    || !isSafeDisplayText(decision.impactSummary)
    || !isIdentifier(cardNonce)
  ) {
    return false;
  }

  return decision.options.every((option, index) => (
    option.key === OPTION_KEYS[index]
    && isSafeDisplayText(option.label)
  ));
}

export function buildDecisionCard(input, cardNonce) {
  const decision = snapshotDecision(input);
  if (!validateInput(decision, cardNonce)) {
    throw new Error('Invalid decision card input');
  }

  const options = decision.options.map(({ key, label }) => ({ key, label }));
  const optionLines = options.map(({ key, label }) => `${key}：${label}`).join('\n');
  const taskId = decision.taskIdProvided ? decision.taskId : '未提供';

  return {
    config: {
      wide_screen_mode: true,
    },
    header: {
      template: 'blue',
      title: {
        tag: 'plain_text',
        content: '天章项目需要决策',
      },
    },
    elements: [
      {
        tag: 'div',
        text: {
          tag: 'plain_text',
          content: `决策编号：${decision.decisionId}\n关联任务：${taskId}`,
        },
      },
      {
        tag: 'div',
        text: {
          tag: 'plain_text',
          content: `问题：${decision.question}`,
        },
      },
      {
        tag: 'div',
        text: {
          tag: 'plain_text',
          content: `选项：\n${optionLines}`,
        },
      },
      {
        tag: 'div',
        text: {
          tag: 'plain_text',
          content: `推荐：${decision.recommendedOption}\n影响：${decision.impactSummary}`,
        },
      },
      {
        tag: 'note',
        elements: [
          {
            tag: 'plain_text',
            content: '选择后将直接登记，旧卡片不会覆盖新决策。',
          },
        ],
      },
      {
        tag: 'action',
        actions: options.map(({ key, label }) => ({
          tag: 'button',
          text: {
            tag: 'plain_text',
            content: `选择 ${key}`,
          },
          type: key === decision.recommendedOption ? 'primary' : 'default',
          value: {
            kind: 'decision_reply',
            decisionId: decision.decisionId,
            optionKey: key,
            cardNonce,
          },
        })),
      },
      {
        tag: 'form',
        name: 'customDecisionForm',
        elements: [
          {
            tag: 'input',
            name: 'customDecision',
            input_type: 'multiline_text',
            placeholder: {
              tag: 'plain_text',
              content: '输入你希望采用的方案（最多 1000 字）',
            },
          },
          {
            tag: 'button',
            action_type: 'form_submit',
            text: {
              tag: 'plain_text',
              content: '提交自定义方案',
            },
            type: 'primary',
            value: {
              kind: 'decision_custom_reply',
              decisionId: decision.decisionId,
              cardNonce,
            },
          },
        ],
      },
      {
        tag: 'note',
        elements: [
          {
            tag: 'plain_text',
            content: `也可直接发消息（长按复制格式）：\n${formatCustomReplyCommand(decision.decisionId)}`,
          },
        ],
      },
    ],
  };
}
