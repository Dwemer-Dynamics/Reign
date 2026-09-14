// Provider-free data for the actual Clan Accords prefab, including long lists and failure states.
export function getClanAccordsSampleData(preset = "") {
  const types = ["Trade", "MutualWatch", "Agricultural", "Artisan", "Garrison"];
  const titles = ["TRADE COOPERATION", "MUTUAL WATCH", "AGRICULTURAL EXCHANGE", "ARTISAN EXCHANGE", "GARRISON COOPERATION"];
  const benefits = ["+50 denars/day to each clan", "+0.1 security/day in both clans’ towns and castles", "+0.2 hearth growth/day in both clans’ villages", "+0.1 prosperity/day in both clans’ towns", "2% lower garrison wages for each clan"];
  const empty = preset === "clan-accords-empty";
  const full = preset === "clan-accords-capacity";
  const confirmation = preset === "clan-accords-confirmation" || preset === "clan-accords-error";
  const rows = empty ? [] : (full ? Array.from({ length: 30 }, (_, i) => [Math.floor(i / 5), i % 5])
    : [[0, 0], [0, 1], [1, 2], [2, 3], [2, 4], [3, 0], [3, 1]]);
  const clans = ["dey Meroc", "Gundaroving", "Pethros", "fen Gruffendoc", "Togaroving", "Argoros"];
  const kingdoms = ["Vlandia", "Sturgia", "Southern Empire", "Battania", "Sturgia", "Independent"];
  const counts = types.map((_, t) => rows.filter(([, type]) => type === t).length);
  const tier = full ? 6 : 4;
  return {
    ClanText: `Your clan · Tier ${tier}`,
    TradeTotal: `+${counts[0] * 50} / day`, SecurityTotal: `+${(counts[1] * .1).toFixed(1)} / day`,
    HearthTotal: `+${(counts[2] * .2).toFixed(1)} / day`, ProsperityTotal: `+${(counts[3] * .1).toFixed(1)} / day`, GarrisonTotal: `-${counts[4] * 2}%`,
    CountText: `${rows.length} agreements · ${new Set(rows.map(([clan]) => clan)).size} partner clans`,
    IsEmpty: empty, IsListEnabled: !confirmation, IsConfirmationOpen: confirmation, CanConfirm: confirmation,
    ConfirmationTitle: "End Trade Cooperation with dey Meroc?",
    ConfirmationText: "Both clans lose: +50 denars/day to each clan\nRelation with each living adult member of dey Meroc decreases by 10.",
    StatusText: preset === "clan-accords-error" ? "The agreement could not be ended. The campaign is changing; please try again." : "",
    FooterText: preset === "clan-accords-error" ? "The agreement could not be ended. The campaign is changing; please try again." : "War ends accords with opposing clans.",
    AgreementCount: rows.length,
    Filters: [{ Name: "All Accords", Capacity: String(rows.length), IsSelected: true, Sprite: "reign_clan_accords_filter" }, ...types.map((type, i) => ({ Name: type === "MutualWatch" ? "Mutual Watch" : type, Capacity: `${counts[i]} / ${tier}`, IsSelected: false, Sprite: "reign_clan_accords_filter_idle" }))],
    Agreements: rows.map(([clan, type], i) => ({
      PartnerClanName: clans[clan], KingdomName: kingdoms[clan], Title: titles[type],
      BenefitText: benefits[type] + (preset === "clan-accords-landless" && type > 0 && type < 4 ? "\nNo eligible player holdings currently." : ""),
      Arrangers: `Arranged by: You & ${["Alary", "Runa", "Ira", "Aeron", "Olek", "Liena"][clan]}`,
      StartedText: "Since Spring 12, 1084", ApplicabilityText: "", BannerId: `clan_accords_banner_${i}`,
      BannerAsset: "assets/court-player-banner.svg", BannerArgs: "", BannerProvider: "BannerImageTextureProvider"
    })),
    $emptyListRationales: { Agreements: "No active agreements have been arranged through conversation." }
  };
}

export const CLAN_ACCORDS_ROOT_KEYS = Object.keys(getClanAccordsSampleData()).filter(key => !key.startsWith("$"));
