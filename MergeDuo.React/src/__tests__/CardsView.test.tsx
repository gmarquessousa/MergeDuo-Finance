// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useEffect } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { CardsView } from '../components/CardsView';
import { FinanceProvider, useFinance } from '../store';
import type { Card, FinanceUser } from '../types';

const { patchCardMock } = vi.hoisted(() => ({
  patchCardMock: vi.fn(),
}));

vi.mock('../api/cards', async () => {
  const actual = await vi.importActual<typeof import('../api/cards')>('../api/cards');
  return {
    ...actual,
    patchCard: patchCardMock,
  };
});

afterEach(() => {
  cleanup();
  localStorage.clear();
  patchCardMock.mockReset();
});

describe('CardsView', () => {
  it('edits card title and billing days with If-Match', async () => {
    const user = userEvent.setup();
    const original = card();
    const updated = card({
      title: 'Nubank Ultravioleta',
      closingDay: 20,
      dueDay: 8,
      etag: 'etag-2',
    });
    patchCardMock.mockResolvedValue(updated);

    render(
      <FinanceProvider>
        <SeedCards cards={[original]} />
        <CardsView accessToken="token" onBack={vi.fn()} onOpenInvoice={vi.fn()} />
      </FinanceProvider>,
    );

    await user.click(await screen.findByRole('button', { name: 'Editar cartão' }));
    const titleInput = screen.getByDisplayValue('Nubank');
    await user.clear(titleInput);
    await user.type(titleInput, 'Nubank Ultravioleta');
    const dayInputs = screen.getAllByRole('spinbutton');
    const [closingInput, dueInput] = dayInputs.slice(-2);
    fireEvent.change(closingInput, { target: { value: '20' } });
    fireEvent.change(dueInput, { target: { value: '8' } });
    await user.click(screen.getByRole('button', { name: 'Salvar' }));

    await waitFor(() => expect(patchCardMock).toHaveBeenCalledWith(
      'token',
      'card-1',
      {
        title: 'Nubank Ultravioleta',
        closingDay: 20,
        dueDay: 8,
        currency: 'BRL',
      },
      'etag-1',
    ));
    expect(await screen.findByText('Nubank Ultravioleta')).toBeInTheDocument();
    expect(screen.getByText('Fecha dia 20 - Vence dia 08')).toBeInTheDocument();
  });
});

function SeedCards({ cards }: { cards: Card[] }) {
  const { setCurrentUserFinance, setCards } = useFinance();
  useEffect(() => {
    setCurrentUserFinance(user());
    setCards(cards);
  }, [cards, setCards, setCurrentUserFinance]);
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

function card(overrides: Partial<Card> = {}): Card {
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
