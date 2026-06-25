import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { TransactionItem } from '../components/TransactionItem';
import type { Transaction } from '../types';

vi.mock('../store', () => ({
  useFinance: () => ({
    currentUser: {
      id: 'user-1',
      name: 'Ana',
    },
  }),
}));

const baseTransaction: Transaction = {
  id: 'tx-1',
  userId: 'user-1',
  date: '2026-05-05',
  category: 'variable_expense',
  description: 'Mercado',
  amount: 120.5,
};

describe('TransactionItem', () => {
  it('abre os detalhes ao clicar na linha do lancamento', async () => {
    const user = userEvent.setup();
    const onOpen = vi.fn();

    render(<TransactionItem tx={baseTransaction} onOpen={onOpen} />);

    await user.click(screen.getByRole('button', { name: /ver detalhes de mercado/i }));

    expect(onOpen).toHaveBeenCalledTimes(1);
    expect(onOpen).toHaveBeenCalledWith(baseTransaction);
  });

  it('nao abre os detalhes quando o usuario clica em editar', async () => {
    const user = userEvent.setup();
    const onOpen = vi.fn();
    const onEdit = vi.fn();

    render(<TransactionItem tx={baseTransaction} onOpen={onOpen} onEdit={onEdit} />);

    await user.click(screen.getByRole('button', { name: /editar/i }));

    expect(onEdit).toHaveBeenCalledTimes(1);
    expect(onEdit).toHaveBeenCalledWith(baseTransaction);
    expect(onOpen).not.toHaveBeenCalled();
  });
});