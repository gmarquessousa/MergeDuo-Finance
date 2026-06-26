import type { TransactionCategory } from '../types';

interface Props {
  category: TransactionCategory;
  size?: number;
  className?: string;
  strokeWidth?: number;
}

export function CategoryIcon({ category, size = 16, className = '', strokeWidth = 2 }: Props) {
  const common = {
    width: size,
    height: size,
    viewBox: '0 0 24 24',
    fill: 'none',
    stroke: 'currentColor',
    strokeWidth,
    strokeLinecap: 'round' as const,
    strokeLinejoin: 'round' as const,
    className,
    'aria-hidden': true,
  };

  switch (category) {
    case 'income':
      return (
        <svg {...common}>
          <path d="M7 14l5-5 5 5" />
          <path d="M12 9v10" />
          <path d="M4 5h16" />
        </svg>
      );

    case 'credit_card':
      return (
        <svg {...common}>
          <rect x="2.5" y="5" width="19" height="14" rx="2.5" />
          <path d="M2.5 10h19" />
          <path d="M6 15.5h3" />
        </svg>
      );

    case 'loan':
      return (
        <svg {...common}>
          <rect x="2.5" y="6" width="19" height="12" rx="2" />
          <circle cx="12" cy="12" r="2.5" />
          <path d="M5.5 9.5h.01" />
          <path d="M18.5 14.5h.01" />
        </svg>
      );

    case 'fixed_expense':
      return (
        <svg {...common}>
          <rect x="3" y="5" width="18" height="16" rx="2" />
          <path d="M3 10h18" />
          <path d="M8 3v4" />
          <path d="M16 3v4" />
          <path d="M14.5 15l1.5 1.5 2.5-2.5" />
        </svg>
      );

    case 'variable_expense':
      return (
        <svg {...common}>
          <path d="M5 8h14l-1.2 11a2 2 0 0 1-2 1.8H8.2a2 2 0 0 1-2-1.8L5 8z" />
          <path d="M9 8V6a3 3 0 0 1 6 0v2" />
        </svg>
      );

    case 'investment':
      return (
        <svg {...common}>
          <path d="M3 17l6-6 4 4 8-8" />
          <path d="M14 7h7v7" />
        </svg>
      );

    default:
      return null;
  }
}
