import { afterEach, describe, expect, it, vi } from 'vitest';
import { patchCard } from '../api/cards';
import { deleteTransactionGroup, getTransactionGroup, patchTransaction } from '../api/transactions';
import { deleteFixedRule, getFixedRulePreview, patchFixedRule } from '../api/fixedRules';

const ORIGINAL_FETCH = globalThis.fetch;

afterEach(() => {
  globalThis.fetch = ORIGINAL_FETCH;
  vi.restoreAllMocks();
});

function jsonResponse(body: unknown, init: ResponseInit = {}) {
  return new Response(JSON.stringify(body), {
    ...init,
    headers: { 'content-type': 'application/json', ...(init.headers ?? {}) },
  });
}

describe('front-end API contracts', () => {
  it('patches cards with PATCH, If-Match and editable billing fields', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(cardResponse({
      title: 'Nubank Ultra',
      closingDay: 20,
      dueDay: 8,
    })));
    globalThis.fetch = fetchMock;

    await patchCard('token', 'card-1', {
      title: 'Nubank Ultra',
      closingDay: 20,
      dueDay: 8,
      currency: 'BRL',
    }, 'etag-1');

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    const headers = init.headers as Headers;
    expect(String(url)).toContain('/cards/card-1');
    expect(init.method).toBe('PATCH');
    expect(headers.get('if-match')).toBe('etag-1');
    expect(JSON.parse(String(init.body))).toEqual({
      title: 'Nubank Ultra',
      closingDay: 20,
      dueDay: 8,
      currency: 'BRL',
    });
  });

  it('patches transaction category, card, purchase date, tags and notes', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(transactionResponse()));
    globalThis.fetch = fetchMock;

    await patchTransaction('token', 'tx-1', '2026-04', {
      description: 'Notebook',
      amount: 2500,
      category: 'credit_card',
      purchaseDate: '2026-04-10',
      cardId: 'card-1',
      tags: ['trabalho'],
      notes: 'Garantia estendida',
    }, 'etag-tx');

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    const headers = init.headers as Headers;
    expect(String(url)).toContain('/transactions/tx-1?ym=2026-04');
    expect(init.method).toBe('PATCH');
    expect(headers.get('if-match')).toBe('etag-tx');
    expect(JSON.parse(String(init.body))).toEqual(expect.objectContaining({
      category: 'credit_card',
      purchaseDate: '2026-04-10',
      cardId: 'card-1',
      tags: ['trabalho'],
      notes: 'Garantia estendida',
    }));
  });

  it('uses transaction group endpoints for installment details and deletion', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse({ groupId: 'grp-1', items: [transactionResponse()] }))
      .mockResolvedValueOnce(jsonResponse({ groupId: 'grp-1', deletedCount: 3, skippedCount: 0 }));
    globalThis.fetch = fetchMock;

    await getTransactionGroup('token', 'grp-1');
    await deleteTransactionGroup('token', 'grp-1');

    expect(String(fetchMock.mock.calls[0][0])).toContain('/transactions/groups/grp-1');
    expect((fetchMock.mock.calls[0][1] as RequestInit).method).toBe('GET');
    expect(String(fetchMock.mock.calls[1][0])).toContain('/transactions/groups/grp-1');
    expect((fetchMock.mock.calls[1][1] as RequestInit).method).toBe('DELETE');
  });

  it('requests fixed rule preview with a bounded date range', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({
      ruleId: 'rule-1',
      active: true,
      from: '2026-04-01',
      to: '2026-06-30',
      items: [],
    }));
    globalThis.fetch = fetchMock;

    await getFixedRulePreview('token', 'rule-1', '2026-04-01', '2026-06-30');

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(String(url)).toContain('/fixed-rules/rule-1/preview?from=2026-04-01&to=2026-06-30');
    expect(init.method).toBe('GET');
  });

  it('patches fixed rules with If-Match', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(fixedRuleResponse()));
    globalThis.fetch = fetchMock;

    await patchFixedRule('token', 'rule-1', {
      description: 'Aluguel',
      amount: 2200,
    }, 'etag-rule');

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    const headers = init.headers as Headers;
    expect(String(url)).toContain('/fixed-rules/rule-1');
    expect(init.method).toBe('PATCH');
    expect(headers.get('if-match')).toBe('etag-rule');
    expect(JSON.parse(String(init.body))).toEqual({
      description: 'Aluguel',
      amount: 2200,
    });
  });

  it('deletes fixed rules with If-Match', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
    globalThis.fetch = fetchMock;

    await deleteFixedRule('token', 'rule-1', 'etag-rule');

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    const headers = init.headers as Headers;
    expect(String(url)).toContain('/fixed-rules/rule-1');
    expect(init.method).toBe('DELETE');
    expect(headers.get('if-match')).toBe('etag-rule');
  });
});

function cardResponse(overrides: Record<string, unknown> = {}) {
  return {
    id: 'card-1',
    title: 'Nubank',
    closingDay: 27,
    dueDay: 5,
    currency: 'BRL',
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    etag: 'etag-1',
    ...overrides,
  };
}

function transactionResponse() {
  return {
    id: 'tx-1',
    userId: 'usr-1',
    yearMonth: '2026-04',
    date: '2026-04-10',
    purchaseDate: '2026-04-10',
    category: 'credit_card',
    kind: 'out',
    description: 'Notebook',
    amount: 2500,
    currency: 'BRL',
    ownerLabel: null,
    cardId: 'card-1',
    cardTitle: 'Nubank',
    fixedRuleId: null,
    installments: null,
    tags: ['trabalho'],
    notes: 'Garantia estendida',
    source: { channel: 'manual' },
    createdAt: '2026-04-10T12:00:00Z',
    updatedAt: '2026-04-10T12:00:00Z',
    etag: 'etag-tx',
  };
}

function fixedRuleResponse() {
  return {
    id: 'rule-1',
    category: 'fixed_expense',
    description: 'Aluguel',
    amount: 2200,
    cardId: null,
    tags: ['casa'],
    schedule: { type: 'calendar_day', day: 5 },
    startsAt: '2026-04-01',
    endsAt: null,
    active: true,
    lastRunAt: null,
    nextRunAt: null,
    createdAt: '2026-04-01T00:00:00Z',
    updatedAt: '2026-04-01T00:00:00Z',
    etag: 'etag-rule',
  };
}
