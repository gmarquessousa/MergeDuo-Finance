import { useEffect, useState } from 'react';

function readOnlineState(): boolean {
  if (typeof navigator === 'undefined') return true;
  return navigator.onLine;
}

export function useOnlineStatus(): boolean {
  const [isOnline, setIsOnline] = useState(readOnlineState);

  useEffect(() => {
    if (typeof window === 'undefined') return undefined;

    const handleOnline = () => setIsOnline(true);
    const handleOffline = () => setIsOnline(false);

    window.addEventListener('online', handleOnline);
    window.addEventListener('offline', handleOffline);

    return () => {
      window.removeEventListener('online', handleOnline);
      window.removeEventListener('offline', handleOffline);
    };
  }, []);

  return isOnline;
}