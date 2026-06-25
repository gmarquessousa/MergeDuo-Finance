// @vitest-environment jsdom
import { afterEach, describe, expect, it } from 'vitest';
import { act, cleanup, render } from '@testing-library/react';
import {
  FinanceProvider,
  transactionCacheKey,
  useFinance,
  type TransactionLoadState,
} from '../store';
import type { Transaction } from '../types';

afterEach(() => {
  cleanup();
  localStorage.clear();
});

describe('FinanceProvider transaction cache', () => {
  it('does not remove entities that are still referenced by another cache key', () => {
    let finance: ReturnType<typeof useFinance> | null = null;

    function Probe() {
      finance = useFinance();
      return null;
    }

    render(
      <FinanceProvider>
        <Probe />
      </FinanceProvider>,
    );

    const bothKey = transactionCacheKey({ yearMonth: '2026-04', owner: 'both' });
    const meKey = transactionCacheKey({ yearMonth: '2026-04', owner: 'me' });
    const a = transaction({ id: 'a', userId: 'me' });
    const b = transaction({ id: 'b', userId: 'partner' });

    act(() => {
      finance!.setTransactions(bothKey, [a, b]);
      finance!.setTransactions(meKey, [a]);
    });
    act(() => {
      finance!.setTransactions(meKey, []);
    });

    expect(finance!.transactions.map((tx) => tx.id).sort()).toEqual(['a', 'b']);
    expect(loadFor(finance!.transactionLoads, bothKey).itemKeys).toHaveLength(2);
    expect(loadFor(finance!.transactionLoads, meKey).itemKeys).toHaveLength(0);
  });

  it('hydrates the last cached finance snapshot for the same user', () => {
    let finance: ReturnType<typeof useFinance> | null = null;

    function Probe() {
      finance = useFinance();
      return null;
    }

    const user = {
      id: 'user-1',
      name: 'Ana',
      registeredAt: '2026-01-01T00:00:00Z',
      startingBalance: 500,
    };
    const aprilKey = transactionCacheKey({ yearMonth: '2026-04', owner: 'me' });

    const firstRender = render(
      <FinanceProvider>
        <Probe />
      </FinanceProvider>,
    );

    act(() => {
      finance!.setCurrentUserFinance(user);
      finance!.setTransactions(aprilKey, [transaction({ id: 'persisted' })]);
      finance!.setCards([{
        id: 'card-1',
        title: 'Visa',
        closingDay: 10,
        dueDay: 17,
        currency: 'BRL',
        createdAt: '2026-04-01T00:00:00Z',
        updatedAt: '2026-04-01T00:00:00Z',
      }]);
    });

    firstRender.unmount();
    finance = null;

    render(
      <FinanceProvider>
        <Probe />
      </FinanceProvider>,
    );

    act(() => {
      finance!.setCurrentUserFinance(user);
    });

    expect(finance!.cards.map((card) => card.id)).toEqual(['card-1']);
    expect(finance!.transactions.map((tx) => tx.id)).toEqual(['persisted']);
    expect(loadFor(finance!.transactionLoads, aprilKey).status).toBe('ready');
  });
});

function loadFor(loads: Record<string, TransactionLoadState>, key: string) {
  const load = loads[key];
  if (!load) throw new Error(`Missing transaction load ${key}`);
  return load;
}

function transaction(overrides: Partial<Transaction>): Transaction {
  return {
    id: 'tx',
    userId: 'me',
    yearMonth: '2026-04',
    date: '2026-04-10',
    category: 'income',
    description: 'Entrada',
    amount: 100,
    createdAt: '2026-04-10T00:00:00Z',
    ...overrides,
  };
}
