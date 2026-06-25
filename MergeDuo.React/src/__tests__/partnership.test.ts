import { describe, expect, it } from 'vitest';
import { toMergePartnerInfo, type PartnershipResponse } from '../api/partnership';

const BASE: PartnershipResponse = {
  id: 'p1',
  partnershipId: 'pship-1',
  status: 'active',
  userId: 'me',
  partnerUserId: 'partner-2',
  partner: { name: 'Maria', handle: '@maria', initials: 'M' } as never,
  startingBalance: 1234.5,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-02T00:00:00Z',
  endedAt: null,
};

describe('toMergePartnerInfo', () => {
  it('preserves the real startingBalance from the API response', () => {
    const info = toMergePartnerInfo(BASE);
    expect(info.startingBalance).toBe(1234.5);
    expect(info.financialDataAvailable).toBe(true);
    expect(info.partnerUserId).toBe('partner-2');
    expect(info.name).toBe('Maria');
  });

  it('falls back to 0 when startingBalance is missing', () => {
    const info = toMergePartnerInfo({ ...BASE, startingBalance: undefined as unknown as number });
    expect(info.startingBalance).toBe(0);
    expect(info.financialDataAvailable).toBe(true);
  });
});
