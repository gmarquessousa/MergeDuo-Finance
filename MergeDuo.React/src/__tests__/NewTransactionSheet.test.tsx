// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { NewTransactionSheet } from '../components/NewTransactionSheet';
import type { Card } from '../types';

afterEach(() => {
  cleanup();
});

describe('NewTransactionSheet', () => {
  it('submits a regular transaction with the selected date and notes', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();

    render(
      <NewTransactionSheet
        open
        date="2026-04-10"
        cards={[]}
        cardsStatus="ready"
        cardsError={null}
        onClose={vi.fn()}
        onNavigateToCards={vi.fn()}
        onSubmit={onSubmit}
      />,
    );

    await user.type(screen.getByLabelText('Descrição'), 'Mercado');
    await user.type(screen.getByLabelText('Valor'), '12345');
    await user.type(screen.getByLabelText('Observações'), 'Compra do mes');
    await user.click(screen.getByRole('button', { name: 'Salvar' }));

    expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({
      date: '2026-04-10',
      category: 'variable_expense',
      description: 'Mercado',
      amount: 123.45,
      notes: 'Compra do mes',
    }));
  });

  it('submits a credit card transaction with card and installments', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();

    render(
      <NewTransactionSheet
        open
        date="2026-04-10"
        cards={[card({ id: 'card-1', title: 'Nubank' })]}
        cardsStatus="ready"
        cardsError={null}
        onClose={vi.fn()}
        onNavigateToCards={vi.fn()}
        onSubmit={onSubmit}
      />,
    );

    await user.click(screen.getByRole('button', { name: /Cart/i }));
    await user.click(screen.getByRole('button', { name: /Nubank/ }));
    await user.click(screen.getByRole('button', { name: '3x' }));
    await user.type(screen.getByLabelText('Descrição'), 'Notebook');
    await user.type(screen.getByLabelText('Valor'), '250000');
    await user.click(screen.getByRole('button', { name: 'Salvar' }));

    expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({
      date: '2026-04-10',
      category: 'credit_card',
      description: 'Notebook',
      amount: 2500,
      cardId: 'card-1',
      installments: 3,
      notes: null,
    }));
  });

  it('keeps the sheet open and clears transient fields on save and new', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();

    render(
      <NewTransactionSheet
        open
        date="2026-04-10"
        cards={[]}
        cardsStatus="ready"
        cardsError={null}
        onClose={vi.fn()}
        onNavigateToCards={vi.fn()}
        onSubmit={onSubmit}
      />,
    );

    await user.type(screen.getByLabelText('Descrição'), 'Padaria');
    await user.type(screen.getByLabelText('Valor'), '1590');
    await user.type(screen.getByLabelText('Observações'), 'Cafe');
    await user.click(screen.getByRole('button', { name: 'Salvar e novo' }));

    expect(onSubmit).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(screen.getByLabelText('Descrição')).toHaveValue(''));
    expect(screen.getByLabelText('Valor')).toHaveValue('');
    expect(screen.getByLabelText('Observações')).toHaveValue('');
    expect(screen.getByLabelText('Data do lançamento')).toBeInTheDocument();
  });

  it('creates a card inline and auto-selects it', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    const onCreateCard = vi.fn().mockResolvedValue(card({ id: 'card-new', title: 'Inter' }));

    render(
      <QuickCardHarness onCreateCard={onCreateCard} onSubmit={onSubmit} />,
    );

    await user.click(screen.getByRole('button', { name: /Cart/i }));
    await user.type(screen.getByPlaceholderText('Nome do cartão'), 'Inter');
    await user.click(screen.getByRole('button', { name: 'Salvar cartão' }));

    await waitFor(() => expect(onCreateCard).toHaveBeenCalledWith({
      title: 'Inter',
      closingDay: 27,
      dueDay: 5,
    }));

    await user.type(screen.getByLabelText('Descrição'), 'Restaurante');
    await user.type(screen.getByLabelText('Valor'), '8990');
    await user.click(screen.getByRole('button', { name: 'Salvar' }));

    expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({
      category: 'credit_card',
      cardId: 'card-new',
      description: 'Restaurante',
    }));
  });
});

function QuickCardHarness({
  onCreateCard,
  onSubmit,
}: {
  onCreateCard: (data: { title: string; closingDay: number; dueDay: number }) => Promise<Card>;
  onSubmit: (data: Parameters<typeof NewTransactionSheet>[0]['onSubmit'] extends (arg: infer T) => unknown ? T : never) => void;
}) {
  const [cards, setCards] = useState<Card[]>([]);
  return (
    <NewTransactionSheet
      open
      date="2026-04-10"
      cards={cards}
      cardsStatus="ready"
      cardsError={null}
      onClose={vi.fn()}
      onNavigateToCards={vi.fn()}
      onCreateCard={async (data) => {
        const created = await onCreateCard(data);
        setCards([created]);
        return created;
      }}
      onSubmit={onSubmit}
    />
  );
}

function card(overrides: Partial<Card> = {}): Card {
  return {
    id: 'card-1',
    title: 'Nubank',
    closingDay: 27,
    dueDay: 5,
    currency: 'BRL',
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    etag: 'etag-card-1',
    ...overrides,
  };
}
