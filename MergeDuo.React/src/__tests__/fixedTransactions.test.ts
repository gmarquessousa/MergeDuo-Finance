import { describe, expect, it } from 'vitest';
import {
  materializeFixedTransactionsBefore,
  materializeFixedTransactionsForCashMonth,
  materializeFixedTransactionsForMonth,
  nextFixedTransactionDate,
} from '../fixedTransactions';
import type { Card, FixedTransactionRule } from '../types';

function rule(overrides: Partial<FixedTransactionRule> = {}): FixedTransactionRule {
  return {
    id: 'fxr_test',
    category: 'fixed_expense',
    description: 'Aluguel',
    amount: 2200,
    schedule: { type: 'calendar_day', day: 15 },
    startsAt: '2026-01-01',
    endsAt: null,
    active: true,
    createdAt: '2026-01-01T00:00:00Z',
    tags: [],
    ...overrides,
  };
}

describe('fixed transaction materialization', () => {
  it('does not materialize an occurrence after endsAt in the same month', () => {
    const [tx] = materializeFixedTransactionsForMonth(
      [rule({ schedule: { type: 'calendar_day', day: 20 }, endsAt: '2026-04-15' })],
      2026,
      3,
    );

    expect(tx).toBeUndefined();
  });

  it('materializes an occurrence exactly on endsAt', () => {
    const [tx] = materializeFixedTransactionsForMonth(
      [rule({ schedule: { type: 'calendar_day', day: 15 }, endsAt: '2026-04-15', tags: ['casa'] })],
      2026,
      3,
    );

    expect(tx?.date).toBe('2026-04-15');
    expect(tx?.tags).toEqual(['casa']);
  });

  it('returns no next occurrence after the end date has passed', () => {
    const next = nextFixedTransactionDate(
      rule({ schedule: { type: 'calendar_day', day: 15 }, endsAt: '2026-04-15' }),
      new Date(2026, 3, 16),
    );

    expect(next).toBeNull();
  });

  it('stops materializing historical months after endsAt', () => {
    const txs = materializeFixedTransactionsBefore(
      [rule({ schedule: { type: 'calendar_day', day: 5 }, endsAt: '2026-02-05' })],
      new Date(2026, 11, 1),
    );

    expect(txs.map((tx) => tx.date)).toEqual(['2026-01-05', '2026-02-05']);
  });

  it('projects fixed credit card rules into the invoice due month', () => {
    const card: Card = {
      id: 'card_test',
      title: 'Card',
      closingDay: 30,
      dueDay: 5,
      currency: 'BRL',
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
    };

    const txs = materializeFixedTransactionsForCashMonth(
      [rule({
        category: 'credit_card',
        cardId: 'card_test',
        schedule: { type: 'calendar_day', day: 31 },
        startsAt: '2026-01-31',
      })],
      [card],
      2026,
      2,
    );

    expect(txs.map((tx) => tx.date)).toEqual(['2026-03-05', '2026-03-05']);
    expect(txs.map((tx) => tx.purchaseDate)).toEqual(['2026-01-31', '2026-02-28']);
  });
});
