import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { TagsView } from '../components/TagsView';
import { FinanceProvider, useFinance } from '../store';

const { getTransactionTagsMock } = vi.hoisted(() => ({
  getTransactionTagsMock: vi.fn(),
}));

vi.mock('../api/transactions', async () => {
  const actual = await vi.importActual<typeof import('../api/transactions')>('../api/transactions');
  return {
    ...actual,
    getTransactionTags: getTransactionTagsMock,
  };
});

afterEach(() => {
  cleanup();
  localStorage.clear();
  getTransactionTagsMock.mockReset();
});

describe('TagsView', () => {
  it('renders tag totals and expands transactions grouped by tag', async () => {
    const user = userEvent.setup();
    let finance: ReturnType<typeof useFinance> | null = null;

    getTransactionTagsMock.mockResolvedValue({
      tags: ['mercado', 'assinatura'],
      items: [
        {
          tag: 'mercado',
          expensesTotal: 120,
          transactionCount: 1,
          transactions: [transactionResponse()],
        },
        {
          tag: 'assinatura',
          expensesTotal: 0,
          transactionCount: 0,
          transactions: [],
        },
      ],
    });

    function Probe() {
      finance = useFinance();
      return null;
    }

    render(
      <FinanceProvider>
        <Probe />
        <TagsView accessToken="token" onBack={vi.fn()} />
      </FinanceProvider>,
    );

    expect(await screen.findByText('Gastos por tags')).toBeInTheDocument();
    expect(screen.getAllByText('R$ 120,00')).toHaveLength(2);
    expect(screen.getByText('assinatura')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /mercado/i }));

    expect(screen.getByText('Mercado Pago')).toBeInTheDocument();
    await waitFor(() => expect(finance!.knownTags).toEqual(['mercado', 'assinatura']));
  });
});

function transactionResponse() {
  return {
    id: 'tx_1',
    userId: 'usr_1',
    yearMonth: '2026-04',
    date: '2026-04-10',
    purchaseDate: null,
    category: 'variable_expense',
    kind: 'out',
    description: 'Mercado Pago',
    amount: 120,
    currency: 'BRL',
    ownerLabel: null,
    cardId: null,
    fixedRuleId: null,
    installments: null,
    tags: ['mercado'],
    notes: null,
    source: { channel: 'manual' },
    createdAt: '2026-04-10T12:00:00Z',
    updatedAt: '2026-04-10T12:00:00Z',
    etag: 'etag-1',
  };
}
