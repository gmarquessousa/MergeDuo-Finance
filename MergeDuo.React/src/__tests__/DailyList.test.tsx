// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useEffect } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { DailyList } from '../components/DailyList';
import { FinanceProvider, useFinance } from '../store';
import type { FinanceUser } from '../types';

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

vi.mock('../useMonthData', async () => {
  const actual = await vi.importActual<typeof import('../useMonthData')>('../useMonthData');
  return {
    ...actual,
    useMonthData: () => ({
      perDay: [
        { day: 1, net: 0, accumulated: 0, investedAccumulated: 0, txs: [] },
      ],
      baseBeforeMonth: 0,
      investedBeforeMonth: 0,
      totalAcumulado: 0,
      totalInvested: 0,
      patrimonioTotal: 0,
      saldoHoje: null,
      investidoHoje: null,
      patrimonioHoje: null,
      monthTotals: { entradas: 0, saidas: 0, aportes: 0, byCategory: {} },
      monthTransactions: [],
    }),
  };
});

vi.mock('../useAggregateSummary', () => ({
  useAggregateMonthSummary: () => ({
    status: 'idle',
    error: null,
    source: null,
    isStale: false,
    computedAt: null,
    totals: null,
    snapshotToday: null,
    dailyBalances: null,
    dailyMovements: null,
    isProjected: false,
    locallyInvalidated: false,
  }),
}));

afterEach(() => {
  cleanup();
  localStorage.clear();
  createTransactionMock.mockReset();
});

describe('DailyList global launch CTA', () => {
  it('opens NewTransactionSheet and refreshes after creation', async () => {
    const user = userEvent.setup();
    const onTransactionMutated = vi.fn();
    createTransactionMock.mockResolvedValue({
      groupId: null,
      items: [transactionResponse()],
    });

    render(
      <FinanceProvider>
        <SeedUser />
        <DailyList
          accessToken="token"
          year={2026}
          monthIdx={3}
          onNavigateToCards={vi.fn()}
          onTransactionMutated={onTransactionMutated}
        />
      </FinanceProvider>,
    );

    await user.click(screen.getAllByRole('button', { name: /Lan/i })[0]);
    expect(await screen.findByText('Novo lançamento')).toBeInTheDocument();

    await user.type(screen.getByLabelText('Descrição'), 'Mercado');
    await user.type(screen.getByLabelText('Valor'), '1000');
    await user.click(screen.getByRole('button', { name: 'Salvar' }));

    await waitFor(() => expect(createTransactionMock).toHaveBeenCalledWith(
      'token',
      expect.objectContaining({
        date: '2026-04-01',
        category: 'variable_expense',
        description: 'Mercado',
        amount: 10,
        notes: null,
      }),
    ));
    expect(onTransactionMutated).toHaveBeenCalledWith(['2026-04']);
  });
});

function SeedUser() {
  const { setCurrentUserFinance } = useFinance();
  useEffect(() => {
    setCurrentUserFinance(user());
  }, [setCurrentUserFinance]);
  return null;
}

function user(): FinanceUser {
  return {
    id: 'usr-1',
    name: 'Teste',
    registeredAt: '2026-01-01T00:00:00Z',
    startingBalance: 0,
  };
}

function transactionResponse() {
  return {
    id: 'tx-1',
    userId: 'usr-1',
    yearMonth: '2026-04',
    date: '2026-04-01',
    purchaseDate: null,
    category: 'variable_expense',
    kind: 'out',
    description: 'Mercado',
    amount: 10,
    currency: 'BRL',
    ownerLabel: null,
    cardId: null,
    cardTitle: null,
    fixedRuleId: null,
    installments: null,
    tags: [],
    notes: null,
    source: { channel: 'manual' },
    createdAt: '2026-04-01T12:00:00Z',
    updatedAt: '2026-04-01T12:00:00Z',
    etag: 'etag-1',
  };
}
