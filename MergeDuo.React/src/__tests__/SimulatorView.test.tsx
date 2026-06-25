import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const { mockUseYearData, mockUseYearAggregateBalances } = vi.hoisted(() => ({
  mockUseYearData: vi.fn(),
  mockUseYearAggregateBalances: vi.fn(),
}));

vi.mock('../useYearData', () => ({
  useYearData: mockUseYearData,
}));

vi.mock('../useYearAggregateBalances', () => ({
  useYearAggregateBalances: mockUseYearAggregateBalances,
}));

import { SimulatorView } from '../components/SimulatorView';

describe('SimulatorView', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-05-06T12:00:00'));
    mockUseYearData.mockReturnValue({
      months: [{ monthIdx: 4, accumulated: 1000, investedAccumulated: 200 }],
      baseBeforeYear: 1000,
      investedBeforeYear: 200,
    });
    mockUseYearAggregateBalances.mockReturnValue(null);
  });

  afterEach(() => {
    mockUseYearData.mockReset();
    mockUseYearAggregateBalances.mockReset();
    vi.useRealTimers();
    document.body.style.overflow = '';
  });

  it('shows patrimonio in the summary card while keeping the monthly table on cash balance', () => {
    render(<SimulatorView year={2026} monthIdx={0} onBack={vi.fn()} />);

    expect(screen.getByText('Patrimônio projetado em dez/2026')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Adicionar simulação' }));
    act(() => {
      vi.runAllTimers();
    });

    fireEvent.click(screen.getByRole('button', { name: /Aporte/ }));
    fireEvent.change(screen.getByLabelText('Descrição'), {
      target: { value: 'Aporte extra' },
    });
    fireEvent.change(screen.getByLabelText('Valor'), {
      target: { value: '10000' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Adicionar à simulação' }));
    act(() => {
      vi.runAllTimers();
    });

    expect(screen.getByText(/Sem simulação:\s*R\$\s*1\.200,00/)).toBeInTheDocument();
    expect(screen.getAllByText(/R\$\s*900,00/).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/−\s*R\$\s*100,00/)).toHaveLength(1);
  });

  it('prefers corrected aggregate balances over raw yearly accumulation when available', () => {
    mockUseYearData.mockReturnValue({
      months: [{ monthIdx: 4, accumulated: 6900, investedAccumulated: 100 }],
      baseBeforeYear: 1000,
      investedBeforeYear: 100,
    });
    mockUseYearAggregateBalances.mockReturnValue(
      new Map([
        [4, { saldo: 5743.11, investido: 100, patrimonio: 5843.11 }],
      ]),
    );

    render(<SimulatorView year={2026} monthIdx={0} onBack={vi.fn()} />);

    expect(screen.getAllByText(/R\$\s*5\.743,11/).length).toBeGreaterThan(0);
    expect(screen.queryByText(/R\$\s*6\.900,00/)).not.toBeInTheDocument();
  });
});