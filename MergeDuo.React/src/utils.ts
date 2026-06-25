export function formatBRL(n: number): string {
  return n.toLocaleString('pt-BR', {
    style: 'currency',
    currency: 'BRL',
    minimumFractionDigits: 2,
  });
}

export function formatBRLCompact(n: number): string {
  const abs = Math.abs(n);
  if (abs >= 1000) {
    const v = (n / 1000).toFixed(abs >= 10000 ? 0 : 1);
    return `R$ ${v.replace('.', ',')}k`;
  }
  return formatBRL(n);
}

export function hasNegativeSign(n: number): boolean {
  return n < 0 || Object.is(n, -0);
}

export function isNeutralZero(n: number): boolean {
  return n === 0 && !Object.is(n, -0);
}

export function daysInMonth(year: number, monthIdx: number): number {
  return new Date(year, monthIdx + 1, 0).getDate();
}

export function monthLabel(year: number, monthIdx: number): string {
  return new Date(year, monthIdx, 1)
    .toLocaleDateString('pt-BR', { month: 'long', year: 'numeric' })
    .replace(/^./, (c) => c.toUpperCase());
}

export function weekdayLabel(year: number, monthIdx: number, day: number): string {
  return new Date(year, monthIdx, day)
    .toLocaleDateString('pt-BR', { weekday: 'short' })
    .replace('.', '');
}

export function isoDate(year: number, monthIdx: number, day: number): string {
  const mm = String(monthIdx + 1).padStart(2, '0');
  const dd = String(day).padStart(2, '0');
  return `${year}-${mm}-${dd}`;
}

export function isToday(year: number, monthIdx: number, day: number): boolean {
  const t = new Date();
  return (
    t.getFullYear() === year &&
    t.getMonth() === monthIdx &&
    t.getDate() === day
  );
}
