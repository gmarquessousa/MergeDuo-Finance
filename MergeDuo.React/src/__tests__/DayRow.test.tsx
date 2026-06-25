import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { DayRow } from '../components/DayRow';
import type { Transaction } from '../types';

const expenseTx: Transaction = {
  id: 'tx-1',
  date: '2026-05-05',
  category: 'variable_expense',
  description: 'Cafe',
  amount: 12,
};

describe('DayRow', () => {
  it('renderiza somente a coluna Total', () => {
    render(
      <DayRow
        year={2026}
        monthIdx={4}
        day={5}
        dayNet={12}
        totalAcumulado={150}
        expanded={false}
        onToggle={vi.fn()}
        transactions={[]}
      />,
    );

    expect(screen.getByText('Total')).toBeInTheDocument();
    expect(screen.queryByText('Acumulado')).not.toBeInTheDocument();
  });

  it('mostra -0 como valor negativo em vermelho', () => {
    render(
      <DayRow
        year={2026}
        monthIdx={4}
        day={5}
        dayNet={-0}
        totalAcumulado={10}
        expanded={false}
        onToggle={vi.fn()}
        transactions={[]}
      />,
    );

    const value = screen.getByText(/−\s*R\$\s*0,00/);

    expect(value).toHaveClass('text-accent-neg');
  });

  it('mantem zero neutro como travessao', () => {
    render(
      <DayRow
        year={2026}
        monthIdx={4}
        day={5}
        dayNet={0}
        totalAcumulado={10}
        expanded={false}
        onToggle={vi.fn()}
        transactions={[]}
      />,
    );

    expect(screen.getByText('—')).toHaveClass('text-ink-muted');
  });

  it('renderiza o marcador de maior saída quando informado', () => {
    render(
      <DayRow
        year={2026}
        monthIdx={4}
        day={5}
        dayNet={-12}
        totalAcumulado={10}
        expanded={false}
        onToggle={vi.fn()}
        transactions={[expenseTx]}
        markerLabel="Maior saída"
      />,
    );

    expect(screen.getByText('Maior saída')).toHaveClass('text-accent-neg');
  });

  it('deixa o total vermelho quando negativo', () => {
    render(
      <DayRow
        year={2026}
        monthIdx={4}
        day={5}
        dayNet={12}
        totalAcumulado={-150}
        expanded={false}
        onToggle={vi.fn()}
        transactions={[]}
      />,
    );

    expect(screen.getByText(/R\$\s*150,00/)).toHaveClass('text-accent-neg');
  });
});