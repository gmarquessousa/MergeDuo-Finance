import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { SummaryHeader } from '../components/SummaryHeader';

describe('SummaryHeader', () => {
  it('does not show the background refresh banner when the summary is already ready', () => {
    render(
      <SummaryHeader
        status="ready"
        error={null}
        patrimonio={1000}
        saldo={700}
        investido={300}
        dailyRunwayStates={[]}
        mesEntradas={500}
        mesSaidas={200}
        mesAportes={100}
        period="monthly"
        periodLabel="Maio 2026"
        isCurrentPeriod
        isProjected={false}
        refreshing
      />,
    );

    expect(screen.queryByText('Atualizando em segundo plano')).not.toBeInTheDocument();
  });

  it('shows the background refresh banner while the displayed summary is updating', () => {
    render(
      <SummaryHeader
        status="updating"
        error={null}
        patrimonio={1000}
        saldo={700}
        investido={300}
        dailyRunwayStates={[]}
        mesEntradas={500}
        mesSaidas={200}
        mesAportes={100}
        period="monthly"
        periodLabel="Maio 2026"
        isCurrentPeriod
        isProjected={false}
        refreshing={false}
      />,
    );

    expect(screen.getByText('Atualizando em segundo plano')).toBeInTheDocument();
  });

  it('labels current-month balances as today values', () => {
    render(
      <SummaryHeader
        status="ready"
        error={null}
        patrimonio={1000}
        saldo={700}
        investido={300}
        dailyRunwayStates={[]}
        mesEntradas={500}
        mesSaidas={200}
        mesAportes={100}
        period="monthly"
        periodLabel="Maio 2026"
        isCurrentPeriod
        isProjected={false}
      />,
    );

    expect(screen.getByText('Patrimônio atual - Maio 2026')).toBeInTheDocument();
    expect(screen.getByText('Saldo hoje')).toBeInTheDocument();
    expect(screen.getByText('Investido hoje')).toBeInTheDocument();
  });
});
