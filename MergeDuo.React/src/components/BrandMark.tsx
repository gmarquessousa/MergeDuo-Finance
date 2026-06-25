import mergeDuoBrandMark from '../assets/mergeduo-brand-mark.png';

export function BrandMark({ className = '' }: { className?: string }) {
  return (
    <img
      src={mergeDuoBrandMark}
      alt=""
      className={`h-6 w-6 shrink-0 object-contain invert dark:invert-0 ${className}`}
      aria-hidden
    />
  );
}
