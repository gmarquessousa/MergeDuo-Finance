// @vitest-environment jsdom
import { act, cleanup, render, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { NetworkError } from '../api/http';
import { OfflineTransactionsProvider, useOfflineTransactions } from '../offlineTransactionsContext';
import { FinanceProvider, useFinance } from '../store';

const { createTransactionMock } = vi.hoisted(() => ({
  createTransactionMock: vi.fn(),
}));

vi.mock('../api/transactions', async () => {
  const actual = await vi.importActual<typeof import('../api/transactions')>('../api/transactions');
  return {
    ...actual,
    createTransaction: createTransactionMock,
  };
});

afterEach(() => {
  cleanup();
  localStorage.clear();
  createTransactionMock.mockReset();
});

describe('OfflineTransactionsProvider', () => {
  it('queues a local optimistic transaction and flushes it when the app comes back online', async () => {
    let finance: ReturnType<typeof useFinance> | null = null;
    let offline: ReturnType<typeof useOfflineTransactions> | null = null;
    const onRemoteCommit = vi.fn();

    function Probe() {
      finance = useFinance();
      offline = useOfflineTransactions();
      return null;
    }

    render(
      <FinanceProvider>
        <OfflineTransactionsProvider accessToken="token" onRemoteCommit={onRemoteCommit}>
          <Probe />
        </OfflineTransactionsProvider>
      </FinanceProvider>,
    );

    act(() => {
      finance!.setCurrentUserFinance({
        id: 'user-1',
        name: 'Ana',
        registeredAt: '2026-01-01T00:00:00Z',
        startingBalance: 500,
      });
    });

    createTransactionMock
      .mockRejectedValueOnce(new NetworkError('Sem conexao.'))
      .mockResolvedValueOnce({
        groupId: null,
        items: [
          {
            id: 'srv-1',
            userId: 'user-1',
            yearMonth: '2026-04',
            date: '2026-04-10',
            purchaseDate: null,
            category: 'variable_expense',
            kind: 'out',
            description: 'Mercado',
            amount: 85.4,
            currency: 'BRL',
            ownerLabel: null,
            cardId: null,
            fixedRuleId: null,
            installments: null,
            tags: ['mercado'],
            notes: null,
            source: { channel: 'manual' },
            createdAt: '2026-04-10T10:00:00Z',
            updatedAt: '2026-04-10T10:00:00Z',
          },
        ],
      });

    await act(async () => {
      const result = await offline!.createTransactionWithOfflineQueue({
        request: {
          category: 'variable_expense',
          date: '2026-04-10',
          description: 'Mercado',
          amount: 85.4,
          currency: 'BRL',
          tags: ['mercado'],
        },
      });

      expect(result.queued).toBe(true);
    });

    expect(offline!.queuedCreates).toBe(1);
    expect(finance!.transactions).toHaveLength(1);
    expect(finance!.transactions[0].pendingSync).toBe(true);
    expect(finance!.transactions[0].localOnly).toBe(true);
    expect(finance!.transactions[0].tags).toEqual(['mercado']);

    await act(async () => {
      window.dispatchEvent(new Event('online'));
    });

    await waitFor(() => {
      expect(offline!.queuedCreates).toBe(0);
    });

    expect(finance!.transactions).toHaveLength(1);
    expect(finance!.transactions[0].id).toBe('srv-1');
    expect(finance!.transactions[0].pendingSync).toBeUndefined();
    expect(finance!.transactions[0].localOnly).toBeUndefined();
    expect(onRemoteCommit).toHaveBeenCalledWith(['2026-04']);
  });
});
