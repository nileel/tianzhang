import assert from 'node:assert/strict';
import test from 'node:test';

import {
  formatCustomReplyCommand,
  normalizeCustomText,
  parseCustomReplyCommand,
} from '../src/custom-reply.mjs';

test('normalizeCustomText normalizes line endings and enforces exact safety bounds', () => {
  assert.equal(normalizeCustomText('  第一行\r\n第二行  '), '第一行\n第二行');
  assert.equal(normalizeCustomText('   '), null);
  assert.equal(normalizeCustomText('x'.repeat(1001)), null);
  assert.equal(normalizeCustomText('😀'.repeat(1000)), '😀'.repeat(1000));
  assert.equal(normalizeCustomText('😀'.repeat(1001)), null);
  assert.equal(normalizeCustomText('ok\u0000bad'), null);
  assert.equal(normalizeCustomText('ok\u2028bad'), null);
  assert.equal(normalizeCustomText({ toString: () => 'unsafe' }), null);
});

test('parseCustomReplyCommand accepts only the explicit decision command', () => {
  assert.deepEqual(
    parseCustomReplyCommand('DEC-20260716-ABC123：自定义 采用双通道\n并迁移旧数据'),
    { decisionId: 'DEC-20260716-ABC123', customText: '采用双通道\n并迁移旧数据' },
  );
  assert.deepEqual(
    parseCustomReplyCommand('DEC-20260716-ABC123: 自定义 采用双通道'),
    { decisionId: 'DEC-20260716-ABC123', customText: '采用双通道' },
  );
  assert.equal(parseCustomReplyCommand('我想采用双通道'), null);
  assert.equal(parseCustomReplyCommand('DEC-20260716-ABC123：采用双通道'), null);
  assert.equal(parseCustomReplyCommand('DEC-20260716-abc123：自定义 采用双通道'), null);
  assert.equal(parseCustomReplyCommand('DEC-20260716-ABC123：自定义    '), null);
});

test('formatCustomReplyCommand emits the copyable format and rejects invalid ids', () => {
  assert.equal(
    formatCustomReplyCommand('DEC-20260716-ABC123'),
    'DEC-20260716-ABC123：自定义 <你的方案>',
  );
  for (const invalid of ['', 'DEC-CANARY-123', 'DEC-20260716-lower', ' DEC-20260716-ABC123']) {
    assert.throws(() => formatCustomReplyCommand(invalid), /Invalid decision id/);
  }
});
