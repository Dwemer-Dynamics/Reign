// Mirrors the pure production ReignCourtLayout policy; contract tests pin both.
export const COURT_LAYOUT = Object.freeze({
  Width: 1518, Height: 854, TopHudInset: 150, BottomHudInset: 52, SideInset: 24
});

export function computeCourtLayout(width, height) {
  const p = COURT_LAYOUT;
  if (!Number.isFinite(width) || !Number.isFinite(height) || width <= 0 || height <= 0)
    return { scale: 1, topInset: p.TopHudInset };
  const hudScale = Math.min(height / 1080, width / (1920 * 0.98));
  const scale = Math.min((width - 2 * p.SideInset * hudScale) / p.Width,
    (height - (p.TopHudInset + p.BottomHudInset) * hudScale) / p.Height);
  return { scale, topInset: p.TopHudInset * hudScale / scale };
}
