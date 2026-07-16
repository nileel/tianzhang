import assert from 'node:assert/strict';
import { createHmac } from 'node:crypto';
import test from 'node:test';

import { parsePrivateConfig, sanitizeError, sha256 } from '../src/config.mjs';
import { buildDecisionCard } from '../src/card.mjs';
import { canonicalize, signEnvelope, verifyEnvelope } from '../src/envelope.mjs';

const HMAC_KEY = Buffer.alloc(32, 0x5a).toString('base64');
const OTHER_HMAC_KEY = Buffer.alloc(32, 0x6b).toString('base64');
const DANGEROUS_DISPLAY_TEXT = /[\p{Cc}\p{Cs}\p{Zl}\p{Zp}\u061c\u200b\u200e\u200f\u202a-\u202e\u2060\u2066-\u2069\ufeff]/u;
const EXPLICIT_REJECTED_CODE_POINTS = [
  0x0000,
  0x0008,
  0x001b,
  0xd800,
  0x2028,
  0x2029,
  0x00ad,
  0x180e,
  0x206a,
  0x206b,
  0x206c,
  0x206d,
  0x206e,
  0x206f,
  0xfff9,
  0xfffa,
  0xfffb,
  0xe0001,
  0xe0020,
];

function makeConfig(overrides = {}) {
  return {
    schemaVersion: 1,
    appId: 'cli_test_app',
    appSecret: 'test-app-secret',
    recipient: {
      type: 'email',
      value: 'operator@example.invalid',
    },
    expectedTenantKey: null,
    pairedOperatorOpenIdHash: null,
    hmacKey: HMAC_KEY,
    stateRoot: 'C:\\Users\\test\\.codex\\automation-state',
    ...overrides,
  };
}

function makeDecision(overrides = {}) {
  return {
    decisionId: 'DEC-20260716-ABC123',
    taskId: 'TQ-057',
    question: '应采用哪种实现方案？',
    options: [
      { key: 'A', label: '方案甲' },
      { key: 'B', label: '方案乙' },
      { key: 'C', label: '方案丙' },
    ],
    recommendedOption: 'B',
    impactSummary: 'A 改动较大；B 风险较低；C 会延期。',
    ...overrides,
  };
}

function captureThrown(action) {
  try {
    action();
  } catch (error) {
    return error;
  }
  assert.fail('Expected action to throw');
}

test('sha256 returns the standard lowercase 64-character digest', () => {
  assert.equal(
    sha256('abc'),
    'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad',
  );
  assert.match(sha256('另一个值'), /^[0-9a-f]{64}$/);
  assert.throws(() => sha256(123), /Invalid input/);
});

test('parsePrivateConfig accepts email/open_id, clones, normalizes, and deeply freezes', () => {
  const input = makeConfig({
    expectedTenantKey: 'tenant-key',
    pairedOperatorOpenIdHash: 'a'.repeat(64),
  });
  const parsed = parsePrivateConfig(input);

  assert.deepEqual(parsed, input);
  assert.notStrictEqual(parsed, input);
  assert.notStrictEqual(parsed.recipient, input.recipient);
  assert.ok(Object.isFrozen(parsed));
  assert.ok(Object.isFrozen(parsed.recipient));

  input.appId = 'mutated';
  input.recipient.value = 'mutated@example.invalid';
  assert.equal(parsed.appId, 'cli_test_app');
  assert.equal(parsed.recipient.value, 'operator@example.invalid');
  assert.throws(() => {
    parsed.recipient.value = 'cannot-change';
  }, TypeError);

  const openId = parsePrivateConfig(makeConfig({
    recipient: { type: 'open_id', value: 'ou_test_operator' },
    stateRoot: '/var/lib/tzg-feishu',
  }));
  assert.equal(openId.recipient.type, 'open_id');
  assert.equal(openId.stateRoot, '/var/lib/tzg-feishu');
});

test('parsePrivateConfig rejects chat_id recipients and non-32-byte HMAC keys', () => {
  assert.throws(
    () => parsePrivateConfig(makeConfig({ recipient: { type: 'chat_id', value: 'oc_test' } })),
    /Invalid private config/,
  );
  assert.throws(
    () => parsePrivateConfig(makeConfig({ hmacKey: Buffer.alloc(31).toString('base64') })),
    /Invalid private config/,
  );
});

test('parsePrivateConfig rejects unknown fields, missing fields, and invalid scalar contracts', async (t) => {
  const cases = [
    ['unknown root field', makeConfig({ unexpected: true })],
    ['unknown recipient field', makeConfig({ recipient: { type: 'email', value: 'a@b.invalid', extra: true } })],
    ['missing schema field', (() => { const value = makeConfig(); delete value.appId; return value; })()],
    ['wrong schema', makeConfig({ schemaVersion: 2 })],
    ['blank app id', makeConfig({ appId: '   ' })],
    ['blank app secret', makeConfig({ appSecret: '' })],
    ['blank recipient', makeConfig({ recipient: { type: 'email', value: ' ' } })],
    ['blank tenant key', makeConfig({ expectedTenantKey: '' })],
    ['uppercase operator hash', makeConfig({ pairedOperatorOpenIdHash: 'A'.repeat(64) })],
    ['short operator hash', makeConfig({ pairedOperatorOpenIdHash: 'a'.repeat(63) })],
    ['relative state root', makeConfig({ stateRoot: '.\\state' })],
    ['drive-relative state root', makeConfig({ stateRoot: 'C:state' })],
    ['non-object input', null],
  ];

  for (const [name, value] of cases) {
    await t.test(name, () => {
      assert.throws(() => parsePrivateConfig(value), /Invalid private config/);
    });
  }
});

test('parsePrivateConfig rejects symbol, hidden, and accessor properties without invoking getters', () => {
  const symbolConfig = makeConfig();
  symbolConfig[Symbol('unknown')] = true;
  assert.throws(() => parsePrivateConfig(symbolConfig), /Invalid private config/);

  const hiddenConfig = makeConfig();
  Object.defineProperty(hiddenConfig, 'hidden', { value: true });
  assert.throws(() => parsePrivateConfig(hiddenConfig), /Invalid private config/);

  const accessorConfig = makeConfig();
  let getterCalls = 0;
  Object.defineProperty(accessorConfig, 'appId', {
    enumerable: true,
    get() {
      getterCalls += 1;
      return 'cli_accessor_app';
    },
  });
  assert.throws(() => parsePrivateConfig(accessorConfig), /Invalid private config/);
  assert.equal(getterCalls, 0);
});

test('parsePrivateConfig requires canonical base64 for an exact 32-byte HMAC key', async (t) => {
  const canonical = Buffer.alloc(32, 0x7f).toString('base64');
  const cases = [
    ['bad alphabet', '!'.repeat(44)],
    ['missing canonical padding', canonical.replace(/=$/, '')],
    ['embedded whitespace', `${canonical.slice(0, 8)}\n${canonical.slice(8)}`],
    ['too long', Buffer.alloc(33).toString('base64')],
  ];

  for (const [name, hmacKey] of cases) {
    await t.test(name, () => {
      assert.throws(() => parsePrivateConfig(makeConfig({ hmacKey })), /Invalid private config/);
    });
  }
});

test('sanitizeError globally redacts literal sensitive values without regular-expression injection', () => {
  const secret = 's3cr.et*?[x]';
  const email = 'owner+test@example.invalid';
  const openId = 'ou_1234567890abcdef';
  const result = sanitizeError(
    new Error(`${secret} ${email} ${openId} ${secret}`),
    [secret, email, openId],
  );

  assert.equal(typeof result, 'string');
  assert.doesNotMatch(result, /s3cr\.et|owner\+test|ou_1234567890abcdef/);
  assert.equal((result.match(/\[REDACTED\]/g) ?? []).length, 4);
});

test('sanitizeError fully redacts overlapping sensitive values in either input order', () => {
  const shorterFirst = sanitizeError(new Error('abcdef'), ['abc', 'abcdef']);
  const longerFirstWithDuplicate = sanitizeError(
    new Error('abcdef'),
    ['abcdef', 'abc', 'abcdef'],
  );

  assert.equal(shorterFirst, '[REDACTED]');
  assert.equal(longerFirstWithDuplicate, '[REDACTED]');
  assert.doesNotMatch(`${shorterFirst} ${longerFirstWithDuplicate}`, /abc|def|abcdef/);
});

test('sanitizeError removes dangerous controls and bidi formatting while preserving safe Unicode', () => {
  const safeUnicode = '中文 Ελληνικά العربية 😀 👩\u200d💻 ❤️\ufe0f';
  const dangerous = '\u0000\u001b\u0008\u202e\u061c\u200e\u200f\u2066\u2069\u200b\u2060\ufeff\ud800\u2028\u2029';
  const result = sanitizeError(new Error(`${safeUnicode}${dangerous}安全结尾`));

  assert.doesNotMatch(result, DANGEROUS_DISPLAY_TEXT);
  assert.doesNotMatch(result, /[\r\n]/);
  assert.match(result, /中文/);
  assert.match(result, /Ελληνικά/);
  assert.match(result, /العربية/);
  assert.match(result, /😀/u);
  assert.ok(result.includes('\u200d'));
  assert.ok(result.includes('\ufe0f'));
  assert.match(result, /安全结尾/);
});

test('sanitizeError normalizes messages and secrets before longest-first redaction', () => {
  const fakeSecret = 'tokenabcdef';
  const outputs = [
    sanitizeError(new Error('before token\u0000abcdef after'), [fakeSecret]),
    sanitizeError(new Error(`before ${fakeSecret} after`), ['token\u202eabcdef']),
    sanitizeError(new Error(`before ${fakeSecret} after`), [`\u0000${fakeSecret}\u2060`]),
    sanitizeError(new Error('before abc\u001bdef after'), ['abc', 'abc\u001bdef', 'abcdef']),
  ];

  for (const result of outputs) {
    assert.doesNotMatch(result, DANGEROUS_DISPLAY_TEXT);
    assert.doesNotMatch(result, /token|abcdef|abc.*def/);
    assert.match(result, /\[REDACTED\]/);
  }
  assert.equal(sanitizeError(new Error('safe text'), ['\u0000', '\u202e']), 'safe text');
});

test('sanitizeError and buildDecisionCard reject an independent explicit Unicode code-point table', async (t) => {
  const rejectedSet = new Set(EXPLICIT_REJECTED_CODE_POINTS);
  for (const codePoint of EXPLICIT_REJECTED_CODE_POINTS) {
    await t.test(`U+${codePoint.toString(16).toUpperCase()}`, () => {
      const character = String.fromCodePoint(codePoint);
      const sanitized = sanitizeError(new Error(`left${character}right`));
      const remaining = [...sanitized].map((value) => value.codePointAt(0));

      assert.ok(remaining.every((value) => !rejectedSet.has(value)));
      assert.notEqual(sanitized, 'leftright');
      assert.doesNotMatch(sanitized, /[\r\n]/);
      assert.throws(
        () => buildDecisionCard(makeDecision({ question: `left${character}right` }), 'nonce-1'),
        /Invalid decision card input/,
      );
    });
  }
});

test('sanitizeError and buildDecisionCard preserve explicitly allowed Unicode formatting', () => {
  const safeUnicode = '中文 Ελληνικά العربية فارسی\u200cزبان 😀 👩\u200d💻 \u2764\ufe0e \u2764\ufe0f';
  assert.equal(sanitizeError(new Error(safeUnicode)), safeUnicode);

  const decision = makeDecision({
    question: safeUnicode,
    options: [
      { key: 'A', label: safeUnicode },
      { key: 'B', label: safeUnicode },
      { key: 'C', label: safeUnicode },
    ],
    impactSummary: safeUnicode,
  });
  const encoded = JSON.stringify(buildDecisionCard(decision, 'nonce-1'));
  for (const allowed of ['\u200c', '\u200d', '\ufe0e', '\ufe0f']) {
    assert.ok(encoded.includes(allowed));
  }
});

test('sanitizeError ignores allowed default-ignorables only while matching secrets', async (t) => {
  const fakeSecret = 'tokenabcdef';
  const defaultIgnorablePattern = /\p{Default_Ignorable_Code_Point}/gu;
  for (const codePoint of [0x200c, 0x200d, 0xfe0e, 0xfe0f]) {
    await t.test(`U+${codePoint.toString(16).toUpperCase()}`, () => {
      const character = String.fromCodePoint(codePoint);
      const messageInserted = sanitizeError(
        new Error(`before token${character}abcdef after`),
        [fakeSecret],
      );
      const secretInserted = sanitizeError(
        new Error(`before ${fakeSecret} after`),
        [`token${character}abcdef`],
      );

      assert.equal(messageInserted, 'before [REDACTED] after');
      assert.equal(secretInserted, 'before [REDACTED] after');
      assert.ok(messageInserted.includes('[REDACTED]'));
      assert.ok(!messageInserted.replace(defaultIgnorablePattern, '').includes(fakeSecret));
      assert.equal(sanitizeError(new Error('safe text'), [character]), 'safe text');
    });
  }
});

test('sanitizeError preserves non-secret ZWNJ, ZWJ, VS15, and VS16 byte-for-byte', () => {
  const samples = [
    'فارسی\u200cزبان',
    '👩\u200d💻',
    '\u2764\ufe0e',
    '\u2764\ufe0f',
  ];
  for (const sample of samples) {
    const result = sanitizeError(new Error(sample));
    assert.ok(Buffer.from(result, 'utf8').equals(Buffer.from(sample, 'utf8')));
  }
});

test('sanitizeError handles empty and non-Error values without echoing raw input', () => {
  assert.equal(sanitizeError(null), 'Unknown error');
  assert.equal(sanitizeError(undefined), 'Unknown error');
  assert.equal(sanitizeError('raw-secret-input'), 'Unknown error');
  assert.equal(sanitizeError({ payload: 'raw-secret-input' }), 'Unknown error');
  assert.equal(sanitizeError(new Error('')), 'Unknown error');
});

test('buildDecisionCard renders the question, A/B/C options, recommendation, and impact safely', () => {
  const decision = makeDecision({
    appSecret: 'must-not-leak',
    hmacKey: 'must-not-leak-either',
    unrelated: 'must-not-be-rendered',
  });
  decision.options[0].secret = 'option-secret';

  const card = buildDecisionCard(decision, 'nonce-123');
  const encoded = JSON.stringify(card);
  const actionElement = card.elements.find((element) => element.tag === 'action');
  const formElement = card.elements.find((element) => element.tag === 'form');

  assert.notStrictEqual(card, decision);
  assert.equal(card.header.title.content, '天章项目需要决策');
  assert.match(encoded, /决策编号：DEC-20260716-ABC123/);
  assert.match(encoded, /关联任务：TQ-057/);
  assert.match(encoded, /应采用哪种实现方案/);
  assert.match(encoded, /方案甲/);
  assert.match(encoded, /方案乙/);
  assert.match(encoded, /方案丙/);
  assert.match(encoded, /推荐.*B/);
  assert.match(encoded, /A 改动较大；B 风险较低；C 会延期/);
  assert.ok(actionElement);
  assert.equal(actionElement.actions.length, 3);
  assert.deepEqual(
    actionElement.actions.map((action) => action.text.content),
    ['选择 A', '选择 B', '选择 C'],
  );
  assert.equal(JSON.stringify(actionElement.actions).includes('方案甲'), false);
  assert.equal(JSON.stringify(actionElement.actions).includes('方案乙'), false);
  assert.equal(JSON.stringify(actionElement.actions).includes('方案丙'), false);
  assert.deepEqual(actionElement.actions.map((action) => action.value), [
    { kind: 'decision_reply', decisionId: decision.decisionId, optionKey: 'A', cardNonce: 'nonce-123' },
    { kind: 'decision_reply', decisionId: decision.decisionId, optionKey: 'B', cardNonce: 'nonce-123' },
    { kind: 'decision_reply', decisionId: decision.decisionId, optionKey: 'C', cardNonce: 'nonce-123' },
  ]);
  for (const action of actionElement.actions) {
    assert.deepEqual(
      Object.keys(action.value).sort(),
      ['cardNonce', 'decisionId', 'kind', 'optionKey'],
    );
  }
  assert.ok(formElement);
  assert.equal(formElement.name, 'customDecisionForm');
  assert.deepEqual(formElement.elements[0], {
    tag: 'input',
    name: 'customDecision',
    input_type: 'multiline_text',
    placeholder: { tag: 'plain_text', content: '输入你希望采用的方案（最多 1000 字）' },
  });
  assert.deepEqual(formElement.elements[1], {
    tag: 'button',
    name: 'submitCustomDecision',
    action_type: 'form_submit',
    text: { tag: 'plain_text', content: '提交自定义方案' },
    type: 'primary',
    value: {
      kind: 'decision_custom_reply',
      decisionId: decision.decisionId,
      cardNonce: 'nonce-123',
    },
  });
  assert.match(encoded, /长按复制格式/);
  assert.match(encoded, /DEC-20260716-ABC123：自定义 <你的方案>/);
  assert.doesNotMatch(encoded, /must-not-leak|option-secret|unrelated|appSecret|hmacKey/i);

  decision.question = 'mutated question';
  decision.options[0].label = 'mutated option';
  assert.doesNotMatch(JSON.stringify(card), /mutated/);
});

test('buildDecisionCard uses a fixed task placeholder and never infers a missing taskId', () => {
  const decision = makeDecision({ taskSummary: 'TQ-MUST-NOT-BE-INFERRED' });
  delete decision.taskId;

  const encoded = JSON.stringify(buildDecisionCard(decision, 'nonce-no-task'));

  assert.match(encoded, /决策编号：DEC-20260716-ABC123/);
  assert.match(encoded, /关联任务：未提供/);
  assert.doesNotMatch(encoded, /TQ-MUST-NOT-BE-INFERRED/);
});

test('buildDecisionCard rejects dangerous characters in every input display field', async (t) => {
  const optionWithLabel = (index, label) => {
    const decision = makeDecision();
    decision.options[index] = { ...decision.options[index], label };
    return decision;
  };
  const cases = [
    ['question newline', makeDecision({ question: '问题\n伪造内容' })],
    ['option A escape', optionWithLabel(0, '方案甲\u001b')],
    ['option B bidi override', optionWithLabel(1, '方案乙\u202e')],
    ['option C zero width', optionWithLabel(2, '方案丙\u200b')],
    ['impact bidi isolate', makeDecision({ impactSummary: '影响\u2066伪造' })],
  ];

  for (const [name, decision] of cases) {
    await t.test(name, () => {
      assert.throws(
        () => buildDecisionCard(decision, 'nonce-1'),
        /Invalid decision card input/,
      );
    });
  }
});

test('buildDecisionCard preserves normal international text, emoji, ZWJ, and VS16', () => {
  const safeText = '正常中文 Ελληνικά العربية 😀 👩\u200d💻 ❤️\ufe0f';
  const decision = makeDecision({
    question: `问题 ${safeText}`,
    options: [
      { key: 'A', label: `甲 ${safeText}` },
      { key: 'B', label: `乙 ${safeText}` },
      { key: 'C', label: `丙 ${safeText}` },
    ],
    impactSummary: `影响 ${safeText}`,
  });

  const encoded = JSON.stringify(buildDecisionCard(decision, 'nonce-1'));
  assert.match(encoded, /正常中文/);
  assert.match(encoded, /Ελληνικά/);
  assert.match(encoded, /العربية/);
  assert.match(encoded, /😀/u);
  assert.ok(encoded.includes('\u200d'));
  assert.ok(encoded.includes('\ufe0f'));
});

test('buildDecisionCard enforces bounded ASCII identifiers', async (t) => {
  const valid = buildDecisionCard(
    makeDecision({ decisionId: 'DEC-20260716-A1', taskId: 'TQ-1' }),
    'nonce-1',
  );
  const validAction = valid.elements.find((element) => element.tag === 'action').actions[0];
  assert.equal(validAction.value.decisionId, 'DEC-20260716-A1');
  assert.equal(validAction.value.cardNonce, 'nonce-1');

  const cases = [
    ['noncanonical decision', makeDecision({ decisionId: 'DEC-1' }), 'nonce-1'],
    ['decision whitespace', makeDecision({ decisionId: 'DEC 1' }), 'nonce-1'],
    ['decision leading punctuation', makeDecision({ decisionId: '-DEC-1' }), 'nonce-1'],
    ['decision too long', makeDecision({ decisionId: `D${'a'.repeat(128)}` }), 'nonce-1'],
    ['task slash', makeDecision({ taskId: 'TQ/1' }), 'nonce-1'],
    ['task unicode', makeDecision({ taskId: '任务-1' }), 'nonce-1'],
    ['nonce slash', makeDecision(), 'nonce/1'],
    ['nonce leading punctuation', makeDecision(), '.nonce-1'],
    ['nonce too long', makeDecision(), `n${'a'.repeat(128)}`],
  ];
  for (const [name, decision, nonce] of cases) {
    await t.test(name, () => {
      assert.throws(
        () => buildDecisionCard(decision, nonce),
        /Invalid decision card input/,
      );
    });
  }
});

test('buildDecisionCard rejects decision accessors without executing getters', async (t) => {
  for (const field of ['decisionId', 'taskId', 'question', 'options', 'recommendedOption', 'impactSummary']) {
    await t.test(field, () => {
      const decision = makeDecision();
      const original = decision[field];
      let getterCalls = 0;
      Object.defineProperty(decision, field, {
        enumerable: true,
        get() {
          getterCalls += 1;
          return original;
        },
      });

      assert.throws(
        () => buildDecisionCard(decision, 'nonce-1'),
        /Invalid decision card input/,
      );
      assert.equal(getterCalls, 0);
    });
  }
});

test('buildDecisionCard rejects option accessors without executing getters', async (t) => {
  for (const field of ['key', 'label']) {
    await t.test(field, () => {
      const decision = makeDecision();
      const original = decision.options[0][field];
      let getterCalls = 0;
      Object.defineProperty(decision.options[0], field, {
        enumerable: true,
        get() {
          getterCalls += 1;
          return original;
        },
      });

      assert.throws(
        () => buildDecisionCard(decision, 'nonce-1'),
        /Invalid decision card input/,
      );
      assert.equal(getterCalls, 0);
    });
  }
});

test('buildDecisionCard rejects blank decision fields and invalid recommendations', async (t) => {
  const cases = [
    ['blank decision id', makeDecision({ decisionId: ' ' }), 'nonce'],
    ['blank task id', makeDecision({ taskId: ' ' }), 'nonce'],
    ['unsafe task id', makeDecision({ taskId: 'TQ-057\nspoofed' }), 'nonce'],
    ['non-string task id', makeDecision({ taskId: 57 }), 'nonce'],
    ['blank question', makeDecision({ question: '' }), 'nonce'],
    ['blank impact', makeDecision({ impactSummary: '' }), 'nonce'],
    ['unknown recommendation', makeDecision({ recommendedOption: 'D' }), 'nonce'],
    ['blank nonce', makeDecision(), '  '],
  ];
  for (const [name, decision, nonce] of cases) {
    await t.test(name, () => {
      assert.throws(() => buildDecisionCard(decision, nonce), /Invalid decision card input/);
    });
  }
});

test('buildDecisionCard requires exactly one ordered A, B, and C option with non-empty labels', async (t) => {
  const cases = [
    ['missing option', makeDecision({ options: makeDecision().options.slice(0, 2) })],
    ['extra option', makeDecision({ options: [...makeDecision().options, { key: 'D', label: 'D' }] })],
    ['wrong order', makeDecision({ options: [makeDecision().options[1], makeDecision().options[0], makeDecision().options[2]] })],
    ['duplicate key', makeDecision({ options: [{ key: 'A', label: 'A1' }, { key: 'A', label: 'A2' }, { key: 'C', label: 'C' }] })],
    ['blank label', makeDecision({ options: [{ key: 'A', label: 'A' }, { key: 'B', label: ' ' }, { key: 'C', label: 'C' }] })],
    ['non-array options', makeDecision({ options: null })],
  ];
  for (const [name, decision] of cases) {
    await t.test(name, () => {
      assert.throws(() => buildDecisionCard(decision, 'nonce'), /Invalid decision card input/);
    });
  }
});

test('canonicalize recursively sorts object keys, preserves arrays, and emits compact JSON', () => {
  const value = {
    b: 1,
    arr: [{ z: 2, a: 1 }, 3],
    a: { d: 4, c: 3 },
  };
  assert.equal(
    canonicalize(value),
    '{"a":{"c":3,"d":4},"arr":[{"a":1,"z":2},3],"b":1}',
  );
});

test('canonicalize rejects unsafe integers while preserving safe numbers deterministically', () => {
  assert.throws(
    () => canonicalize(Number.MAX_SAFE_INTEGER + 1),
    /Cannot canonicalize value/,
  );
  assert.throws(
    () => canonicalize(-(Number.MAX_SAFE_INTEGER + 1)),
    /Cannot canonicalize value/,
  );
  assert.throws(() => canonicalize(1e100), /Cannot canonicalize value/);

  assert.equal(canonicalize(Number.MAX_SAFE_INTEGER), String(Number.MAX_SAFE_INTEGER));
  assert.equal(canonicalize(Number.MIN_SAFE_INTEGER), String(Number.MIN_SAFE_INTEGER));
  assert.equal(canonicalize(1.5), '1.5');
  assert.equal(canonicalize(-0), '0');
});

test('canonicalize rejects unsupported values, cycles, exotic prototypes, and dangerous keys', async (t) => {
  const cycle = {};
  cycle.self = cycle;
  const cases = [
    ['undefined root', undefined],
    ['undefined property', { value: undefined }],
    ['undefined array item', [undefined]],
    ['function', { value() {} }],
    ['symbol', Symbol('value')],
    ['non-finite NaN', NaN],
    ['non-finite Infinity', { value: Infinity }],
    ['bigint', 1n],
    ['cycle', cycle],
    ['date prototype', new Date(0)],
    ['__proto__ key', JSON.parse('{"__proto__":{"polluted":true}}')],
    ['prototype key', { prototype: {} }],
    ['constructor key', { constructor: {} }],
  ];

  for (const [name, value] of cases) {
    await t.test(name, () => {
      assert.throws(() => canonicalize(value), /Cannot canonicalize value/);
    });
  }
});

test('signEnvelope signs a cloned payload with a lowercase HMAC-SHA256 signature', () => {
  const payload = { z: 2, nested: { value: 'original' }, a: 1 };
  const envelope = signEnvelope(payload, HMAC_KEY);
  const expectedSignature = createHmac('sha256', Buffer.from(HMAC_KEY, 'base64'))
    .update(canonicalize(payload), 'utf8')
    .digest('hex');

  assert.deepEqual(Object.keys(envelope).sort(), ['payload', 'schemaVersion', 'signature']);
  assert.equal(envelope.schemaVersion, 1);
  assert.equal(envelope.signature, expectedSignature);
  assert.match(envelope.signature, /^[0-9a-f]{64}$/);
  assert.notStrictEqual(envelope.payload, payload);
  assert.notStrictEqual(envelope.payload.nested, payload.nested);

  payload.nested.value = 'mutated';
  assert.equal(envelope.payload.nested.value, 'original');
});

test('verifyEnvelope returns a deeply frozen clone independent of the envelope', () => {
  const wireEnvelope = structuredClone(signEnvelope({ nested: { value: 'original' }, list: [1, 2] }, HMAC_KEY));
  const verified = verifyEnvelope(wireEnvelope, HMAC_KEY);

  assert.notStrictEqual(verified, wireEnvelope.payload);
  assert.ok(Object.isFrozen(verified));
  assert.ok(Object.isFrozen(verified.nested));
  assert.ok(Object.isFrozen(verified.list));
  wireEnvelope.payload.nested.value = 'mutated-after-verify';
  assert.equal(verified.nested.value, 'original');
  assert.throws(() => {
    verified.list.push(3);
  }, TypeError);
});

test('verifyEnvelope fails closed for payload tampering and a wrong HMAC key', () => {
  const envelope = structuredClone(signEnvelope({ choice: 'A', privateValue: 'DO_NOT_LEAK' }, HMAC_KEY));
  envelope.payload.choice = 'B';

  const tamperError = captureThrown(() => verifyEnvelope(envelope, HMAC_KEY));
  assert.match(tamperError.message, /Envelope verification failed/);
  assert.doesNotMatch(tamperError.message, /DO_NOT_LEAK|choice|[A-Za-z0-9+/]{43}=/);

  const wrongKeyError = captureThrown(() => verifyEnvelope(signEnvelope({ choice: 'A' }, HMAC_KEY), OTHER_HMAC_KEY));
  assert.match(wrongKeyError.message, /Envelope verification failed/);
});

test('signEnvelope and verifyEnvelope reject invalid HMAC encodings', async (t) => {
  const invalidKeys = [
    Buffer.alloc(31).toString('base64'),
    HMAC_KEY.replace(/=$/, ''),
    'not-base64!',
  ];
  for (const key of invalidKeys) {
    await t.test(`invalid key ${invalidKeys.indexOf(key) + 1}`, () => {
      assert.throws(() => signEnvelope({ value: 1 }, key), /Envelope operation failed/);
      assert.throws(() => verifyEnvelope(signEnvelope({ value: 1 }, HMAC_KEY), key), /Envelope verification failed/);
    });
  }
});

test('verifyEnvelope rejects malformed structures and signature formats before comparison', async (t) => {
  const valid = signEnvelope({ choice: 'A' }, HMAC_KEY);
  const cases = [
    ['null envelope', null],
    ['wrong schema', { ...valid, schemaVersion: 2 }],
    ['missing payload', { schemaVersion: 1, signature: valid.signature }],
    ['extra field', { ...valid, extra: true }],
    ['short signature', { ...valid, signature: 'ab' }],
    ['uppercase signature', { ...valid, signature: valid.signature.toUpperCase() }],
    ['non-hex signature', { ...valid, signature: 'g'.repeat(64) }],
  ];

  for (const [name, envelope] of cases) {
    await t.test(name, () => {
      assert.throws(() => verifyEnvelope(envelope, HMAC_KEY), /Envelope verification failed/);
    });
  }
});
