const DECISION_ID_PATTERN = /^DEC-[0-9]{8}-[A-Z0-9]+$/;
const COMMAND_PATTERN = /^\s*(DEC-[0-9]{8}-[A-Z0-9]+)\s*[：:]\s*自定义[ \t]+([\s\S]+?)\s*$/u;
const UNSAFE_CHARACTER_PATTERN = /[\p{Cc}\p{Cf}\p{Cs}\p{Zl}\p{Zp}]/u;

export function normalizeCustomText(value) {
  if (typeof value !== 'string') {
    return null;
  }
  const normalized = value.replace(/\r\n?/g, '\n').trim();
  const characters = [...normalized];
  if (characters.length < 1 || characters.length > 1000) {
    return null;
  }
  if (characters.some((character) => (
    character !== '\n'
    && character !== '\t'
    && UNSAFE_CHARACTER_PATTERN.test(character)
  ))) {
    return null;
  }
  return normalized;
}

export function parseCustomReplyCommand(value) {
  if (typeof value !== 'string') {
    return null;
  }
  const match = COMMAND_PATTERN.exec(value);
  if (match === null) {
    return null;
  }
  const customText = normalizeCustomText(match[2]);
  if (customText === null) {
    return null;
  }
  return Object.freeze({ decisionId: match[1], customText });
}

export function formatCustomReplyCommand(decisionId) {
  if (typeof decisionId !== 'string' || !DECISION_ID_PATTERN.test(decisionId)) {
    throw new Error('Invalid decision id');
  }
  return `${decisionId}：自定义 <你的方案>`;
}
