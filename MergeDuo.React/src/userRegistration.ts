export interface RegistrationMonth {
  year: number;
  monthIdx: number;
  start: Date;
}

export function getUserRegistrationMonth(registeredAt?: string | null): RegistrationMonth {
  const fallback = new Date();
  const parsed = registeredAt ? new Date(`${registeredAt}T00:00`) : fallback;
  const date = Number.isNaN(parsed.getTime()) ? new Date() : parsed;

  return {
    year: date.getFullYear(),
    monthIdx: date.getMonth(),
    start: new Date(date.getFullYear(), date.getMonth(), 1),
  };
}

export function monthIndex(year: number, monthIdx: number) {
  return year * 12 + monthIdx;
}

export function isBeforeRegistrationMonth(
  year: number,
  monthIdx: number,
  registration = getUserRegistrationMonth(),
) {
  return monthIndex(year, monthIdx) < monthIndex(registration.year, registration.monthIdx);
}

export function isTransactionOnOrAfterRegistrationMonth(
  date: string,
  registration = getUserRegistrationMonth(),
) {
  const parsed = new Date(`${date}T00:00`);
  if (Number.isNaN(parsed.getTime())) return false;

  return parsed.getTime() >= registration.start.getTime();
}
