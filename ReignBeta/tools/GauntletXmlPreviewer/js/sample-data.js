import { getGovernmentHearingSampleData, GOVERNMENT_ROOT_KEYS } from "./government-hearing-sample-data.js";
import { getClanAccordsSampleData, CLAN_ACCORDS_ROOT_KEYS } from "./clan-accords-sample-data.js";
import { getTavernHouseSampleData, TAVERN_HOUSE_ROOT_KEYS } from "./tavern-house-sample-data.js";
import { previewActionRichText } from "./action-text.js";
// Shared source portraits are always present. Generated chest/zoom derivatives
// are optional, so using source.png keeps the preview deterministic instead of
// silently rendering empty portrait frames on a fresh workspace.
const portrait = (folder) => `../../PortraitCache/_shared/${folder}/source.png`;

export const ASSETS = {
  mengus: portrait("Mengus (lord_5_7)"),
  ira: portrait("Ira (lord_1_37)"),
  corein: portrait("Corein (lord_5_10)"),
  caladog: portrait("Caladog (lord_5_1)"),
  rhagaea: portrait("Rhagaea (lord_1_14)"),
  derthert: portrait("Derthert (lord_4_1)"),
  abagai: portrait("Abagai (lord_6_12)"),
  spymasterFullBody: "assets/spymaster-full-body-preview.png",
  aeron: portrait("Aeron (lord_5_16)"),
  adalindis: portrait("Adalindis (lord_4_28_1)"),
  lucon: portrait("Lucon (lord_1_1)"),
  eventArt: "../../EventArt/feast_empire/toasts_and_table_talk.png",
  castleMapEmpire: "../../GUI/SpriteParts/ui_reignbeta_castle_layout/reign_castle_map_empire.png",
  wildernessForest: "../../EventArt/generated_wilderness/forest/generic.png",
  wildernessBackground: "../../GUI/SpriteParts/ui_reignbeta_event/reign_wilderness_event_background.png",
  courtBackground: "../../GUI/SpriteParts/ui_reignbeta_court/reign_court_throne_background.png",
  rulerPetitionCourt: "../../GUI/UiCalibration/reference-scenes/ruler-petition-throne-viewpoint-vlandia.png",
  courtPlayerBanner: "assets/court-player-banner.svg",
  individualChatBackground: "../../GUI/SpriteParts/ui_reignbeta_individual/reign_individual_chat_background.png",
  partyChatBackground: "../../GUI/SpriteParts/ui_reignbeta_party/reign_party_chat_background.png"
};

const portraitFields = (id, asset, status = "Available") => ({
  PortraitId: id,
  PortraitAsset: asset,
  PortraitAdditionalArgs: "",
  PortraitTextureProviderName: "ReignPortraitProvider",
  PortraitCacheKey: `preview_${id}`,
  HasGeneratedPortrait: Boolean(asset),
  PortraitCropImageWidth: 168,
  PortraitCropImageHeight: 126,
  Status: status
});

const emptyCastleSlots = (count) => Array.from({ length: count }, () => ({
  PortraitCacheKey: "",
  HasPortrait: false,
  CanOpen: false
}));

const INDIVIDUAL_CHAT_LINES = [
  {
    Speaker: "Conversation",
    Text: "Private conversation with Mengus fen Gruffendoc.",
    IsPlayerLine: false,
    IsNpcLine: false,
    IsSystemLine: true
  },
  {
    Speaker: "Mengus fen Gruffendoc",
    Text: "You asked what the western clans expect from this gathering. They expect proof that your promises will survive the winter, not merely another speech beneath royal banners.",
    IsPlayerLine: false,
    IsNpcLine: true,
    IsSystemLine: false
  },
  {
    Speaker: "Aeric fen Seanel",
    Text: "Then tell them the grain escorts leave within seven days, and that I will ride with the first column myself.",
    IsPlayerLine: true,
    IsNpcLine: false,
    IsSystemLine: false
  },
  {
    Speaker: "Mengus fen Gruffendoc",
    Text: "That answer may satisfy them. Caladog will still demand names, numbers, and the road each company is sworn to defend.",
    IsPlayerLine: false,
    IsNpcLine: true,
    IsSystemLine: false
  }
];

export const SAMPLE_DATA = {
  Title: "A Pact Proclaimed Before the Lords of Calradia",
  DateText: "Summer 3, 1088",
  LocationText: "Location: the imperial hall at Amitatys",
  SelectedSummary: "3 characters selected · 2 active · 7 available",
  Description: "A reusable stress-test description long enough to reveal narrow columns, unexpected wrapping, and clipped copy.",

  IsOverlayVisible: true,
  ShowPhaseControls: true,
  ShowWildernessArt: false,
  EventTitle: "Artisan Salon",
  PhaseTitle: "Browsing and Bargaining",
  PhaseStatus: "Phase 3 of 5 | Attendees 12",
  PhaseDescription: "Guests browse, bargain, compliment, and quietly compete through patronage.",
  EventImageId: "feast_empire_toasts_and_table_talk",
  EventImageAsset: ASSETS.eventArt,
  EventImageText: "A Feast Beneath Uneasy Banners\nToasts and Table Talk",
  EventArtSize: 876,
  EventArtRowHeight: 896,
  AttendeeListTopMargin: 48,
  AttendeeListBottomMargin: 74,
  AttendeeTitleTopMargin: 12,
  EventArtTopMargin: 48,
  EventBackgroundImageId: "event_background",
  BackgroundImageId: "event_background",
  BackgroundAsset: ASSETS.eventArt,
  BackgroundAdditionalArgs: "",
  BackgroundTextureProviderName: "ReignEventArtProvider",
  CultureTint: "#6B431B2A",

  PlayerName: "Aeric fen Seanel",
  PlayerSubtitle: "Clan Seanel · sworn to Battania",
  PlayerInfo: "Location: Marunath",
  PlayerPortraitId: "player_portrait",
  PlayerPortraitAsset: ASSETS.aeron,
  PlayerPortraitAdditionalArgs: "",
  PlayerPortraitTextureProviderName: "ReignPortraitProvider",
  PlayerPortraitCropImageWidth: 168,
  PlayerPortraitCropImageHeight: 220,
  PlayerPortraitCacheKey: "preview_player_portrait",
  CurrentLocation: "Main Hall",
  SelectedLocation: "",
  StatusText: "Select your portrait, then choose a room.",
  PlayerTokenArmed: false,
  CanMove: false,
  MoveButtonText: "MOVE TO LOCATION",
  EmptySlots: [{}, {}, {}, {}],
  CastleGardenSlots: emptyCastleSlots(4),
  TrainingYardSlots: emptyCastleSlots(4),
  StableCourtyardSlots: emptyCastleSlots(4),
  GuestBedroomSlotsTop: emptyCastleSlots(8),
  GuestBedroomSlotsBottom: emptyCastleSlots(8),
  NobleSolarSlots: emptyCastleSlots(4),
  InnerCourtyardSlots: emptyCastleSlots(3),
  MainHallSlots: emptyCastleSlots(3),
  RoyalBedroomSlots: emptyCastleSlots(3),
  ThroneRoomSlots: emptyCastleSlots(4),
  LibrarySlots: emptyCastleSlots(3),
  DiningChamberSlots: emptyCastleSlots(3),
  PortraitGallerySlots: emptyCastleSlots(4),
  ChapelSlots: emptyCastleSlots(3),
  BathsSlots: emptyCastleSlots(3),
  BattlementSlots: emptyCastleSlots(4),

  NpcName: "Mengus fen Gruffendoc",
  NpcSubtitle: "Battanian lord · House Gruffendoc",
  NpcInfo: "Location: Marunath",
  NpcPortraitId: "mengus_portrait",
  NpcPortraitAsset: ASSETS.mengus,
  NpcPortraitAdditionalArgs: "",
  NpcPortraitTextureProviderName: "ReignPortraitProvider",
  NpcPortraitCropImageWidth: 168,
  NpcPortraitCropImageHeight: 220,

  IsBusy: false,
  BusyText: "Waiting for Bannerlord Reign...",
  InputEnabled: true,
  InputText: "",
  ChatScrollVersion: 7,
  IsPortraitZoomVisible: false,
  IsPregnancyWarningVisible: false,
  IsPortraitPreviewVisible: false,
  ZoomFrameWidth: 620,
  ZoomFrameHeight: 720,
  ZoomPortraitImageWidth: 560,
  ZoomPortraitImageHeight: 680,
  ZoomPortraitId: "zoom_portrait",
  ZoomPortraitAsset: ASSETS.mengus,
  ZoomPortraitAdditionalArgs: "",
  ZoomPortraitTextureProviderName: "ReignPortraitProvider",
  PortraitPreviewFrameWidth: 620,
  PortraitPreviewFrameHeight: 720,
  PortraitPreviewImageWidth: 560,
  PortraitPreviewImageHeight: 680,
  PortraitPreviewId: "preview_portrait",
  PortraitPreviewAsset: ASSETS.corein,
  PortraitPreviewAdditionalArgs: "",
  PortraitPreviewTextureProviderName: "ReignPortraitProvider",
  PortraitPreviewName: "Corein of the fen Gruffendoc",
  PortraitCropImageWidth: 168,
  PortraitCropImageHeight: 220,

  AuthorityText: "ROYAL COURT",
  CourtTitle: "THE ROYAL COURT OF VLANDIA",
  PlayerClan: "Clan dey Meroc",
  PlayerBannerId: "player_clan_banner",
  PlayerBannerAsset: ASSETS.courtPlayerBanner,
  PlayerBannerArgs: "",
  PlayerBannerProvider: "BannerTableauTextureProvider",
  NextDocketText: "Next docket: 8:00 AM",
  UseLargeCourtLayout: false,
  LargeHomeVisible: false,
  ReferenceHomeVisible: true,
  HomeVisible: true,
  TabContentVisible: false,
  DetailVisible: false,
  AudienceVisible: false,
  ActionsVisible: false,
  ScreenTitle: "COURT",
  ScreenSubtitle: "THE ROYAL COURT AT PRAVEND",
  TimeText: "10:42 AM",
  StatusText: "Court session connected · petitions synchronized",
  DetailTitle: "The Guildmasters' Petition for Protected Grain Caravans",
  DetailBody: "Merchants request crown protection for a threatened supply route. In return they offer reduced wartime prices, priority provisioning for the garrison, and a bond forfeited if their promised deliveries fail.",
  DetailMeta: "AWAITING DECISION   ·   Due before sunset",
  AudienceSpeaker: "Lord Varmund of the Western Marches",
  AudienceText: "My liege, the guilds speak of charity, but their ledgers speak more plainly. Grant the escort only if their bond can feed every household that will bleed to protect it.",
  AudienceEmotion: "Measured, but openly skeptical",
  AudienceInput: "Answer Lord Varmund before the assembled court…",
  AudienceHasPortrait: true,
  AudiencePortraitId: "varmund_portrait",
  AudiencePortraitAsset: ASSETS.derthert,
  AudiencePortraitAdditionalArgs: "",
  AudiencePortraitTextureProviderName: "ReignPortraitProvider",
  AudiencePortraitCacheKey: "court_preview_varmund",
  PortraitCacheKey: "court_preview_portrait",

  Tabs: [
    { Label: "COURT", LockText: "", IsActive: true },
    { Label: "LANDS", LockText: "", IsActive: false },
    { Label: "SUBJECTS", LockText: "", IsActive: false },
    { Label: "REPUTATION", LockText: "", IsActive: false },
    { Label: "DIPLOMACY", LockText: "", IsActive: false },
    { Label: "AMBASSADORS", LockText: "", IsActive: false },
    { Label: "SPYMASTER", LockText: "", IsActive: false },
    { Label: "MILITARY", LockText: "", IsActive: false }
  ],
  Counters: [
    { Symbol: "G", Label: "FUNDS", Value: "84.5K", Trend: "+152 /day", IconSprite: "reign_court_status_funds", IsPresence: false, IsStrength: false, IsNotStrength: true },
    { Symbol: "I", Label: "INFLUENCE", Value: "3,726", Trend: "+97 /day", IconSprite: "reign_court_status_influence", IsPresence: false, IsStrength: false, IsNotStrength: true },
    { Symbol: "R", Label: "RENOWN", Value: "1,245", Trend: "+1.4 /day", IconSprite: "reign_court_status_renown", IsPresence: false, IsStrength: false, IsNotStrength: true },
    { Symbol: "S", Label: "SUPPLY", Value: "12.4K", Trend: "+210 /day", IconSprite: "reign_court_status_supply", IsPresence: false, IsStrength: false, IsNotStrength: true },
    { Symbol: "M", Label: "STRENGTH", Value: "2,155", Trend: "+63 /day", IconSprite: "reign_court_status_strength", IsPresence: false, IsStrength: true, IsNotStrength: false },
    { Symbol: "C", Label: "COURT PRESENCE", Value: "+18", Trend: "public standing", IconSprite: "reign_court_status_presence", IsPresence: true, IsStrength: false, IsNotStrength: true }
  ],
  Lands: [
    { Title: "Pravend", Subtitle: "Food 245 / 300  ·  Grain 410", Status: "PROSPEROUS", Meta: "Prosperity 2,450  +3.1/day" },
    { Title: "Jaculan", Subtitle: "Food 188 / 250  ·  Grain 290", Status: "STABLE", Meta: "Prosperity 1,120  +1.2/day" },
    { Title: "Galend", Subtitle: "Food 205 / 270  ·  Grain 350", Status: "PROSPEROUS", Meta: "Prosperity 2,010  +2.4/day" },
    { Title: "Charas", Subtitle: "Food 76 / 240  ·  Grain 82", Status: "SHORTAGE", Meta: "Prosperity 870  -1.3/day" },
    { Title: "Lageta", Subtitle: "Food 191 / 260  ·  Grain 315", Status: "STABLE", Meta: "Prosperity 1,680  +1.6/day" }
  ],
  // The player-facing Royal Docket has not been implemented yet.
  // Keep the preview aligned with the live Court screen until it is.
  DailyAgenda: [],
  People: [
    { Title: "Lord Varmund", Subtitle: "Vassal\nLoyalty: 45", Meta: "Today", HasPortrait: true, ...portraitFields("varmund_portrait", ASSETS.derthert) },
    { Title: "Empress Rhagaea", Subtitle: "Foreign sovereign\nOpinion: +18", Meta: "Before noon", HasPortrait: true, ...portraitFields("rhagaea_portrait", ASSETS.rhagaea) },
    { Title: "Master Eronys the Elder", Subtitle: "Treasurer\nThree favors owed", Meta: "Today", HasPortrait: true, ...portraitFields("eronys_portrait", ASSETS.lucon) },
    { Title: "Spymaster Abagai", Subtitle: "Spymaster\nIntelligence ready", Meta: "Today", HasPortrait: true, ...portraitFields("abagai_portrait", ASSETS.abagai) }
  ],
  DiplomaticStatus: [
    { Title: "Kingdom of Vlandia", Status: "FRIENDLY" },
    { Title: "Grand Principality of Sturgia", Status: "NEUTRAL" },
    { Title: "Aserai Sultanate", Status: "WARY" },
    { Title: "Khuzait Khanate", Status: "HOSTILE" },
    { Title: "Southern Empire", Status: "UNFRIENDLY" }
  ],
  AmbassadorCards: [
    { Title: "Ambassador Lucan", Subtitle: "Posted in Vlandia", Status: "POSTED" },
    { Title: "Ambassador Theron", Subtitle: "Traveling to distant Tyal", Status: "TRAVELING" },
    { Title: "Envoy Adalindis of Ocs Hall", Subtitle: "Awaiting formal credentials", Status: "PENDING" }
  ],
  ShowEmptyState: false,
  CanRemove: true,
  Ambassadors: [
    { KingdomName: "Western Empire", Name: "Mitela", ClanName: "Clan Comnos", PortraitId: "ira_portrait", PortraitAsset: ASSETS.ira, PortraitArgs: "", PortraitProvider: "ReignPortraitProvider", PortraitCacheKey: "ambassador_ira", HasPortrait: true, BannerId: "player_clan_banner", BannerAsset: ASSETS.courtPlayerBanner, BannerArgs: "", BannerProvider: "BannerTableau", IsSelected: true },
    { KingdomName: "Khuzait Khanate", Name: "Abagai", ClanName: "Clan Khergit", PortraitId: "abagai_portrait", PortraitAsset: ASSETS.abagai, PortraitArgs: "", PortraitProvider: "ReignPortraitProvider", PortraitCacheKey: "ambassador_abagai", HasPortrait: true, BannerId: "player_clan_banner", BannerAsset: ASSETS.courtPlayerBanner, BannerArgs: "", BannerProvider: "BannerTableau", IsSelected: false },
    { KingdomName: "Battania", Name: "Corein", ClanName: "fen Gruffendoc", PortraitId: "corein_portrait", PortraitAsset: ASSETS.corein, PortraitArgs: "", PortraitProvider: "ReignPortraitProvider", PortraitCacheKey: "ambassador_corein", HasPortrait: true, BannerId: "player_clan_banner", BannerAsset: ASSETS.courtPlayerBanner, BannerArgs: "", BannerProvider: "BannerTableau", IsSelected: false },
    { KingdomName: "Vlandia", Name: "Adalindis", ClanName: "House dey Rothad", PortraitId: "adalindis_portrait", PortraitAsset: ASSETS.adalindis, PortraitArgs: "", PortraitProvider: "ReignPortraitProvider", PortraitCacheKey: "ambassador_adalindis", HasPortrait: true, BannerId: "player_clan_banner", BannerAsset: ASSETS.courtPlayerBanner, BannerArgs: "", BannerProvider: "BannerTableau", IsSelected: false },
    { KingdomName: "Southern Empire", Name: "Rhagaea", ClanName: "House Pethros", PortraitId: "rhagaea_portrait", PortraitAsset: ASSETS.rhagaea, PortraitArgs: "", PortraitProvider: "ReignPortraitProvider", PortraitCacheKey: "ambassador_rhagaea", HasPortrait: true, BannerId: "player_clan_banner", BannerAsset: ASSETS.courtPlayerBanner, BannerArgs: "", BannerProvider: "BannerTableau", IsSelected: false }
  ],
  Rumors: [
    { Title: "Unrest is rising among the dockworkers of Charas.", Status: "MEDIUM" },
    { Title: "Lord Iric of Galend quietly seeks a marriage alliance.", Status: "LOW" },
    { Title: "Aserai nobles are purchasing horses and whispering of war.", Status: "HIGH" }
  ],
  TabRows: [
    { Title: "Petition of the Pravend Guildmasters", Subtitle: "A scheduled audience concerning food deliveries, caravan protection, and a forfeitable commercial bond.", Status: "AWAITING", Meta: "Due today", HasPortrait: true, ...portraitFields("guildmaster_portrait", ASSETS.adalindis) },
    { Title: "Steward of the Royal Household", Subtitle: "Major court office. Appointment and performance rules will be supplied by the selected tab's later design pass.", Status: "VACANT", Meta: "Appointment required", HasPortrait: false, ...portraitFields("", "") },
    { Title: "A promise made to Lord Varmund", Subtitle: "Pay 1,000 denars before the recorded deadline. Terms remain hashed and revision guarded.", Status: "UNRESOLVED", Meta: "14 days", HasPortrait: true, ...portraitFields("varmund_portrait", ASSETS.derthert) }
  ],
  Actions: [
    { Label: "ACCEPT WITH BOND", Description: "Approve the escort only after the guilds deposit the full forfeitable bond.", IsEnabled: true, StateText: "READY" },
    { Label: "REFUSE PETITION", Description: "Decline crown protection and leave the route to private guards.", IsEnabled: true, StateText: "READY" }
  ],

  ChatLines: [
    { Speaker: "Event", Text: "The Artisan Salon begins.", IsPlayerLine: false, IsNpcLine: false, IsSystemLine: true },
    { Speaker: "Event", Text: "Aevonos Aurelorides, Aevonara Aurelorides approach you.", IsPlayerLine: false, IsNpcLine: false, IsSystemLine: true },
    { Speaker: "Event", Text: "The event moves to the next phase: Demonstrations.", IsPlayerLine: false, IsNpcLine: false, IsSystemLine: true },
    { Speaker: "Event", Text: "The event moves to the next phase: Browsing and Bargaining.", IsPlayerLine: false, IsNpcLine: false, IsSystemLine: true },
    { Speaker: "Event", Text: "Aevonos Aurelorides, Aevonara Aurelorides approach you.", IsPlayerLine: false, IsNpcLine: false, IsSystemLine: true }
  ],
  PartyMembers: [
    { Name: "Mengus fen Gruffendoc", IsSelected: true, ...portraitFields("mengus_portrait", ASSETS.mengus, "Active") },
    { Name: "Ira of the Southern Empire", IsSelected: true, ...portraitFields("ira_portrait", ASSETS.ira, "Watching") },
    { Name: "Corein, Daughter of Caladog", IsSelected: false, ...portraitFields("corein_portrait", ASSETS.corein, "Listening") },
    { Name: "Adalindis of Ocs Hall", IsSelected: false, ...portraitFields("adalindis_portrait", ASSETS.adalindis, "Waiting") }
  ],
  Attendees: [
    { Name: "Mengus fen Gruffendoc", Subtitle: "Battanian lord", IsActive: true, ...portraitFields("mengus_portrait", ASSETS.mengus, "Active conversation") },
    { Name: "Empress Rhagaea Pethros", Subtitle: "Sovereign of the Southern Empire", IsActive: false, ...portraitFields("rhagaea_portrait", ASSETS.rhagaea, "Available") },
    { Name: "Corein, Daughter of High King Caladog", Subtitle: "Battanian noblewoman", IsActive: false, ...portraitFields("corein_portrait", ASSETS.corein, "Available") }
  ],
  ActiveParticipants: [
    { Name: "Mengus fen Gruffendoc", Subtitle: "Battania · House Gruffendoc", IsActive: true, ...portraitFields("mengus_portrait", ASSETS.mengus, "Active") },
    { Name: "Aevonos Aurelorides of Amitatys", Subtitle: "Western Empire · House Comnos", IsActive: true, ...portraitFields("aevonos_portrait", ASSETS.lucon, "Active") }
  ],
  AvailableAttendees: [
    { Name: "Empress Rhagaea Pethros", Subtitle: "Southern Empire · House Pethros", IsActive: false, ...portraitFields("rhagaea_portrait", ASSETS.rhagaea, "Available") },
    { Name: "Corein, Daughter of High King Caladog", Subtitle: "Battania · fen Gruffendoc", IsActive: false, ...portraitFields("corein_portrait", ASSETS.corein, "Available") },
    { Name: "Abagai of the Khergit Borderlands", Subtitle: "Khuzait Khanate · Baltait", IsActive: false, ...portraitFields("abagai_portrait", ASSETS.abagai, "Available") },
    { Name: "Adalindis of Ocs Hall and the Western March", Subtitle: "Vlandia · House dey Rothad", IsActive: false, ...portraitFields("adalindis_portrait", ASSETS.adalindis, "Available") },
    { Name: "High King Caladog fen Gruffendoc", Subtitle: "Battania · Royal House", IsActive: false, ...portraitFields("caladog_portrait", ASSETS.caladog, "Available") }
  ],
  Contacts: [
    { Name: "Mengus fen Gruffendoc", Subtitle: "Battanian lord · last met at Marunath", IsSelected: true, HasUnread: true, UnreadText: "2 unread", ...portraitFields("mengus_portrait", ASSETS.mengus, "Friendly · 42") },
    { Name: "Empress Rhagaea Pethros", Subtitle: "Southern Empire · diplomatic correspondence", IsSelected: false, HasUnread: false, UnreadText: "", ...portraitFields("rhagaea_portrait", ASSETS.rhagaea, "Cordial · 18") },
    { Name: "Corein, Daughter of High King Caladog", Subtitle: "Battania · trusted correspondent", IsSelected: false, HasUnread: true, UnreadText: "1 unread", ...portraitFields("corein_portrait", ASSETS.corein, "Friendly · 51") },
    { Name: "Lady Adalindis of Ocs Hall", Subtitle: "Vlandia · court and trade matters", IsSelected: false, HasUnread: false, UnreadText: "", ...portraitFields("adalindis_portrait", ASSETS.adalindis, "Neutral · 4") }
  ],
  SelectedName: "Mengus fen Gruffendoc",
  SelectedSubtitle: "Battanian lord · correspondence carried by trusted courier",

  ShowTarget: true,
  Outcome: "A Defensive Compact Has Been Proclaimed",
  Summary: "Aeric fen Seanel and Empress Rhagaea have agreed to protect the Amitatys grain road for one campaigning season. Both parties gain safer commerce, but the compact may alarm neighboring rulers who see an alliance where its signatories describe only practical cooperation.",
  Terms: "Joint patrols begin within seven days. Aeric supplies 120 mounted troops; Rhagaea supplies 180 infantry and the route's quartermasters. Command alternates every thirty days. Either party may withdraw after giving fourteen days' notice.",
  ActorName: "Aeric fen Seanel",
  ActorKingdom: "Battania · Clan Seanel",
  ActorReason: "Seeks secure grain deliveries before the western garrisons exhaust their stores.",
  ActorPortraitId: "actor_portrait",
  ActorPortraitAsset: ASSETS.aeron,
  ActorPortraitArgs: "",
  ActorPortraitProvider: "ReignPortraitProvider",
  ActorPortraitCacheKey: "diplomacy_actor_aeric",
  ActorBannerId: "actor_banner",
  ActorBannerArgs: "",
  ActorBannerProvider: "BannerTableau",
  TargetName: "Empress Rhagaea Pethros",
  TargetKingdom: "Southern Empire · House Pethros",
  TargetReason: "Needs a dependable western route that does not depend on Lucon's goodwill.",
  TargetPortraitId: "target_portrait",
  TargetPortraitAsset: ASSETS.rhagaea,
  TargetPortraitArgs: "",
  TargetPortraitProvider: "ReignPortraitProvider",
  TargetPortraitCacheKey: "diplomacy_target_rhagaea",
  TargetBannerId: "target_banner",
  TargetBannerArgs: "",
  TargetBannerProvider: "BannerTableau"
};

export const ASSET_BY_IMAGE_ID = {
  player_portrait: ASSETS.aeron,
  mengus_portrait: ASSETS.mengus,
  ira_portrait: ASSETS.ira,
  corein_portrait: ASSETS.corein,
  caladog_portrait: ASSETS.caladog,
  aeron_portrait: ASSETS.aeron,
  rhagaea_portrait: ASSETS.rhagaea,
  abagai_portrait: ASSETS.abagai,
  spymaster_portrait: ASSETS.abagai,
  adalindis_portrait: ASSETS.adalindis,
  actor_portrait: ASSETS.aeron,
  target_portrait: ASSETS.rhagaea,
  preview_portrait: ASSETS.corein,
  zoom_portrait: ASSETS.mengus,
  event_background: ASSETS.eventArt,
  feast_empire_toasts_and_table_talk: ASSETS.eventArt,
  "reigneventart|feast_empire|toasts_and_table_talk": ASSETS.eventArt,
  generated_wilderness_forest: ASSETS.wildernessForest,
  generated_wilderness_background: ASSETS.wildernessBackground,
  "reigneventart|castle_map|empire": ASSETS.castleMapEmpire,
  "reigneventart|war_council|map": "../../GUI/SpriteParts/ui_reignbeta_war_council/reign_war_council_calradia.png",
  "reigneventart|war_council|outer_frame": "../../GUI/SpriteParts/ui_reignbeta_war_council/reign_war_council_outer_frame.png",
  "reigneventart|war_council|raven_scroll": "../../GUI/SpriteParts/ui_reignbeta_war_council/reign_war_council_raven_scroll.png",
  "reigneventart|war_council|panel_frame": "../../GUI/SpriteParts/ui_reignbeta_war_council/reign_war_council_panel_frame.png",
  "reigneventart|war_council|panel_frame_tall": "../../GUI/SpriteParts/ui_reignbeta_war_council/reign_war_council_panel_frame_tall.png",
  "reigneventart|war_council|panel_frame_overlay": "../../GUI/SpriteParts/ui_reignbeta_war_council/reign_war_council_panel_frame_overlay.png"
};

// Bannerlord replaces these TextureWidget images by widget identity at runtime
// (see PortraitPatch.TryApplyChatBackground). The previewer mirrors that
// contract so the XML binding used as a provider hook does not become the
// visible portrait asset in the browser.
export const ASSET_BY_WIDGET_ID = {
  ReignIndividualChatBackground: ASSETS.individualChatBackground,
  WarCouncilMap: "../../GUI/SpriteParts/ui_reignbeta_war_council/reign_war_council_calradia.png",
  ActorAIPortrait: ASSETS.aeron,
  TargetAIPortrait: ASSETS.rhagaea
};

const PREVIEW_PRESET_OVERRIDES = {
  "court-life-international": courtLifePreview("INTERNATIONAL", "A Border Complaint", [
    { Name: "Lady Adalindis", Subtitle: "Your noble", ...portraitFields("adalindis_portrait", ASSETS.adalindis) },
    { Name: "Rhagaea's Envoy", Subtitle: "Foreign ambassador", ...portraitFields("rhagaea_portrait", ASSETS.rhagaea) }
  ], "My ruler asks that you hear the merchants before granting compensation. We can compare their accounts here.", "Review the complaint before confirming a settlement.", "SERIOUS"),
  "court-life-family": courtLifePreview("FAMILY VISIT", "A Small Discovery", [
    { Name: "Your young child", Subtitle: "Child, age 4", HasPortrait: false, PortraitId: "", PortraitCacheKey: "", PortraitAsset: "" }
  ], "Look! The beetle has tiny feet. Can we find its house after court?", "Spend time together; plans for later remain a personal conversation."),
  "court-life-patronage": courtLifePreview("PATRONAGE", "Stories for the Road", [
    { Name: "Mira, storyteller of Zeonica", Subtitle: "Storyteller", HasPortrait: false, PortraitId: "", PortraitCacheKey: "", PortraitAsset: "" }
  ], "I can begin with a verse here, or take the tale through your towns. Which audience should I prepare for?", "Kingdom-wide: Praise (8000 gold)", "AT COURT", {
    GoldGrantVisible: true, CanGrantWithGold: true, GoldGrantLabel: "KINGDOM-WIDE: PRAISE (8000 GOLD)",
    PurposeText: "+3 noble relation toward the ruler per noble reached. Delivery: 7 day(s). Current eligible audience: 24 nobles."
  }),
  "court-life-visitors": courtLifePreview("NOBLE VISITORS", "A Visiting Household", [
    { Name: "Lord Mengus", Subtitle: "Parent", ...portraitFields("mengus_portrait", ASSETS.mengus) },
    { Name: "Lady Adalindis", Subtitle: "Parent", ...portraitFields("adalindis_portrait", ASSETS.adalindis) },
    { Name: "Lady Corein", Subtitle: "Adult child, age 23", ...portraitFields("corein_portrait", ASSETS.corein) },
    { Name: "Lady Abagai", Subtitle: "Adult child, age 21", ...portraitFields("abagai_portrait", ASSETS.abagai) }
  ], "We thank you for receiving our household. We will remain in the city for five days, and hope to attend your court again.", "Welcome the household or conclude the audience."),
  "court-history": {
    HomeVisible: false,
    LargeHomeVisible: false,
    ReferenceHomeVisible: false,
    TabContentVisible: true,
    DetailVisible: true,
    AudienceVisible: false,
    ActionsVisible: false,
    ScreenTitle: "COURT HISTORY",
    ScreenSubtitle: "ROYAL COURT AT ZEONICA",
    DetailTitle: "Petition of Temeon the Brewer",
    DetailMeta: "GRANTED · 2 SEPTEMBER 1092",
    DetailBody: "The crown approved emergency food relief for Zeocorys. Forty measures were committed from the capital stores, and the ruling was entered into the permanent court record.",
    TabRows: [
      { Title: "Temeon the Brewer", Subtitle: "Food relief for Zeocorys was granted from the capital stores.", Status: "GRANTED", Meta: "2 September 1092", HasPortrait: true, ...portraitFields("temeon_portrait", ASSETS.mengus) },
      { Title: "Emergency Chancellor", Subtitle: "Lady Adalindis assumed the seals during the Chancellor's incapacity.", Status: "RECORDED", Meta: "1 September 1092", HasPortrait: true, ...portraitFields("emergency_chancellor_portrait", ASSETS.adalindis) },
      { Title: "Garrison Escort Returned", Subtitle: "The randomly selected Zeonica escort returned after completing its service.", Status: "COMPLETE", Meta: "31 August 1092", HasPortrait: false, ...portraitFields("", "") }
    ],
    Actions: []
  },
  "pregnancy-warning": {
    IsPregnancyWarningVisible: true,
    InputEnabled: false,
    IsBusy: true,
    BusyText: "Choose whether to proceed."
  },
  "training-yard-empty": {
    TrainerCount: 0,
    TroopCount: 0,
    Trainers: [],
    Troops: [],
    SelectedTrainerName: "NO TRAINER SELECTED",
    SelectedTrainerDetail: "No eligible adult NPC is available at this keep.",
    LeadershipText: "LEADERSHIP —",
    WeaponText: "BEST WEAPON —",
    RateText: "0 XP PER TROOP / HOUR",
    ElapsedText: "ELAPSED  0 HOURS",
    DeliveredText: "DELIVERED  0 XP",
    StatusText: "No eligible trainer or upgradeable troop is available.",
    CanTrain: false,
    CanStop: false,
    HasSelectedTrainer: false,
    HasSelectedGeneratedPortrait: false,
    PortraitAsset: ""
  },
  "training-yard-active": {
    IsTraining: true,
    CanTrain: false,
    CanStop: true,
    ElapsedText: "ELAPSED  37 HOURS",
    DeliveredText: "DELIVERED  48,372 XP",
    StatusText: "Training continues; campaign food, wages, quests, and world simulation are advancing."
  },
  "spymaster-people": {
    IsIntelligence: true,
    IsSubterfuge: false,
    IsReputation: false,
    IsArchive: false,
    ShowOperationPanel: true,
    IsLands: false,
    IsPeople: true,
    ShowPeopleGroups: true,
    PeopleGroupText: "FOREIGN NOBLES",
    TargetName: "Branara",
    TargetDetail: "Foreign noble · House dey Meroc",
    ActionName: "Investigate relationships",
    OperationDetail: "900 denars  •  3.0 days  •  Success 68%  •  Detection 14%  •  ROUTINE",
    AssessmentText: "ROUTINE"
  },
  "spymaster-subterfuge": {
    IsIntelligence: false,
    IsSubterfuge: true,
    IsReputation: false,
    IsArchive: false,
    ShowOperationPanel: true,
    IsLands: false,
    IsPeople: false,
    ShowPeopleGroups: false,
    ShowSocialItem: false,
    TargetName: "Akkalat",
    TargetDetail: "FOREIGN — TOWN · Tigit; governor Chambui.",
    ActionName: "Disrupt food",
    OperationDetail: "7,500 denars  •  7.0 days  •  Success 36%  •  Detection 47%  •  Difficult  •  Capacity 0/3",
    AssessmentText: "DIFFICULT",
    IsOrdinaryAssessment: false,
    IsChallengingAssessment: true
  },
  "spymaster-reputation": {
    IsIntelligence: false,
    IsSubterfuge: false,
    IsReputation: true,
    IsArchive: false,
    ShowOperationPanel: true,
    IsLands: false,
    IsPeople: false,
    ShowPeopleGroups: false,
    ShowSocialItem: true,
    SocialScopeText: "FOREIGN LORDS",
    SocialItemName: "Select rumor or reputation",
    TargetName: "Aeric fen Seanel",
    TargetDetail: "Ruler of Battania",
    ActionName: "Mitigate one of your rumors",
    OperationDetail: "1,200 denars  •  4.0 days  •  Success 61%  •  Detection 18%  •  GUARDED",
    AssessmentText: "GUARDED"
  },
  "spymaster-archive": {
    IsIntelligence: false,
    IsSubterfuge: false,
    IsReputation: false,
    IsArchive: true,
    ShowOperationPanel: false,
    IsLands: false,
    IsPeople: false,
    ShowPeopleGroups: false,
    ReportText: "Agents confirmed the garrison rotation, granary reserves, and western gate watch schedule. No operative was exposed.",
    Reports: [
      { Title: "Intelligence: Ab Comer Castle", StateText: "COMPLETED", DateText: "12th of Summer, 1092", IsSelected: true },
      { Title: "Rumor inquiry: Branara", StateText: "COMPLETED", DateText: "8th of Summer, 1092", IsSelected: false },
      { Title: "Counterintelligence: Zeonica", StateText: "COMPLETED", DateText: "1st of Summer, 1092", IsSelected: false }
    ]
  },
  wilderness: {
    ShowPhaseControls: false,
    ShowWildernessArt: true,
    EventTitle: "Wilderness Encounter",
    PhaseTitle: "A Voice Beyond the Treeline",
    PhaseStatus: "FOREST TRACK · LATE AFTERNOON",
    PhaseDescription: "A chance meeting interrupts the road before the light fades beneath the trees.",
    EventImageId: "generated_wilderness_forest",
    EventImageAsset: ASSETS.wildernessForest,
    EventImageText: "A Stranger at the Treeline\nForest Road · Late Afternoon",
    EventBackgroundImageId: "generated_wilderness_background",
    BackgroundImageId: "generated_wilderness_background",
    BackgroundAsset: ASSETS.wildernessBackground,
    AttendeeListTopMargin: 492,
    AttendeeListBottomMargin: 16,
    AttendeeTitleTopMargin: 452,
    EventArtTopMargin: 12,
    ActiveParticipants: [
      { Name: "Adalindis of Ocs Hall and the Western March", Subtitle: "Vlandia · House dey Rothad", IsActive: true, ...portraitFields("adalindis_portrait", ASSETS.adalindis, "Speaking") }
    ],
    AvailableAttendees: [
      { Name: "Mengus fen Gruffendoc", Subtitle: "Battania · House Gruffendoc", IsActive: false, ...portraitFields("mengus_portrait", ASSETS.mengus, "Nearby") },
      { Name: "Corein, Daughter of High King Caladog", Subtitle: "Battania · fen Gruffendoc", IsActive: false, ...portraitFields("corein_portrait", ASSETS.corein, "Nearby") }
    ],
    ChatLines: [
      { Speaker: "Event", Text: "Your party slows as movement stirs beyond the treeline.", IsPlayerLine: false, IsNpcLine: false, IsSystemLine: true },
      { Speaker: "Adalindis", Text: "My lord, I had not expected another company on this road before nightfall.", IsPlayerLine: false, IsNpcLine: true, IsSystemLine: false },
      { Speaker: "Event", Text: "The forest falls quiet while both parties decide whether to approach.", IsPlayerLine: false, IsNpcLine: false, IsSystemLine: true }
    ]
  }
};

const PREFAB_SAMPLE_OVERRIDES = {
  "ReignTavernHouseScreen.xml": getTavernHouseSampleData(),
  "ReignClanAccordsScreen.xml": getClanAccordsSampleData(),
  "AIPortraitsMemoriesBook.xml": {
    TitleText: "MEMORIES BOOK",
    CounterText: "1 / 3",
    CaptionText: "A formal pact is proclaimed before the assembled lords of Calradia.",
    HasMemories: true,
    MemoryBookImageId: "reigneventart|feast_empire|toasts_and_table_talk",
    MemoryItems: [
      { ListText: "A Pact Proclaimed Before the Lords of Calradia", IsSelected: true },
      { ListText: "The Council Supper at Zeonica", IsSelected: false },
      { ListText: "A Quiet Road Beyond the Southern Gate", IsSelected: false }
    ]
  },
  "ReignAmbassadorScreen.xml": {
    AmbassadorCount: SAMPLE_DATA.Ambassadors.length,
    PlayerBannerId: "player_clan_banner",
    PlayerBannerAsset: ASSETS.courtPlayerBanner,
    PlayerBannerArgs: "",
    PlayerBannerProvider: "BannerTableauTextureProvider",
    ForeignAdvisor: {
      Name: "Branara",
      Status: "AVAILABLE",
      HasPortrait: true,
      PortraitCacheKey: "preview_foreign_advisor_branara",
      PortraitId: "corein_portrait",
      PortraitAsset: ASSETS.corein,
      PortraitAdditionalArgs: "",
      PortraitTextureProviderName: "ReignPortraitProvider"
    },
    Ambassadors: SAMPLE_DATA.Ambassadors.map((ambassador) => ({
      ...ambassador,
      StatusText: ambassador.Status,
      CanDismiss: true,
      HasCharacterModel: false,
      AmbassadorModel: {
        HasCharacterModel: false,
        BannerCodeText: "",
        BodyProperties: "",
        CharStringId: "",
        EquipmentCode: "",
        IsFemale: false,
        MountCreationKey: "",
        StanceIndex: 0,
        ArmorColor1: "#473421FF",
        ArmorColor2: "#8C733DFF",
        Race: 0,
        IsTableauEnabled: false
      }
    })),
    ShowEmptyState: false,
    StatusText: "Resident envoys are available for official audience."
  },
  "ReignFamilyChambersScreen.xml": {
    Title: "FAMILY CHAMBERS",
    Subtitle: "THE ROYAL HOUSEHOLD AT ZEONICA",
    Adults: [
      { Name: "Gwydan", Detail: "Spouse · present in the royal household", Relation: "RELATION 82", IsSelected: true, ...portraitFields("mengus_portrait", ASSETS.mengus) },
      { Name: "Elideth", Detail: "Sister · economic advisor", Relation: "RELATION 61", IsSelected: false, ...portraitFields("adalindis_portrait", ASSETS.adalindis) }
    ],
    Children: [
      { Name: "Aeron", AgeText: "AGE 12", Parents: "Gwydan and the sovereign", IsSelected: false, ...portraitFields("aeron_portrait", ASSETS.aeron) },
      { Name: "Ceryn", AgeText: "AGE 7", Parents: "Gwydan and the sovereign", IsSelected: true, ...portraitFields("corein_portrait", ASSETS.corein) }
    ],
    SelectionText: "2 family members selected",
    StatusText: "Choose family members to enter the chambers.",
    IsBusy: false,
    CanEnterScene: true
  },
  "ReignTrainingYardScreen.xml": {
    Title: "TRAINING YARD",
    TrainerHeading: "TRAINERS",
    TroopHeading: "PARTY TROOPS",
    SelectedTrainerName: "Tacteos",
    SelectedTrainerDetail: "Companion · travelling with the main party",
    LeadershipText: "LEADERSHIP 150",
    WeaponText: "POLEARM 200",
    RateText: "58 XP PER TROOP / HOUR",
    ElapsedText: "ELAPSED  11 HOURS",
    DeliveredText: "DELIVERED  9,628 XP",
    StatusText: "Tacteos is drilling every troop that can still gain upgrade XP.",
    IsTraining: false,
    CanTrain: true,
    CanStop: false,
    HasSelectedTrainer: true,
    HasSelectedGeneratedPortrait: true,
    SelectedPortraitCacheKey: "preview_training_yard_tacteos",
    SelectedPortraitId: "mengus_portrait",
    SelectedPortraitAdditionalArgs: "",
    SelectedPortraitTextureProviderName: "ReignPortraitProvider",
    PortraitAsset: ASSETS.mengus,
    TrainerCount: 10,
    TroopCount: 12,
    Trainers: [
      { Name: "Tacteos", Detail: "Leadership 150 · Polearm 200", Rate: "58 XP / TROOP / HOUR", IsSelected: true, CanSelect: true, ...portraitFields("mengus_portrait", ASSETS.mengus) },
      { Name: "Corein, Daughter of High King Caladog", Detail: "Leadership 108 · Two Handed 178", Rate: "47 XP / TROOP / HOUR", IsSelected: false, CanSelect: true, ...portraitFields("corein_portrait", ASSETS.corein) },
      { Name: "Aeron the Far-Travelled Shield Brother", Detail: "Leadership 72 · Bow 124", Rate: "32 XP / TROOP / HOUR", IsSelected: false, CanSelect: true, ...portraitFields("aeron_portrait", ASSETS.aeron) },
      { Name: "Adalindis", Detail: "Leadership 94 · Crossbow 132", Rate: "37 XP / TROOP / HOUR", IsSelected: false, CanSelect: true, ...portraitFields("adalindis_portrait", ASSETS.adalindis) },
      { Name: "Caladog", Detail: "Leadership 181 · Two-Handed 210", Rate: "65 XP / TROOP / HOUR", IsSelected: false, CanSelect: true, ...portraitFields("caladog_portrait", ASSETS.caladog) },
      { Name: "Rhagaea", Detail: "Leadership 196 · One-Handed 188", Rate: "64 XP / TROOP / HOUR", IsSelected: false, CanSelect: true, ...portraitFields("rhagaea_portrait", ASSETS.rhagaea) },
      { Name: "Derthert", Detail: "Leadership 167 · Polearm 176", Rate: "57 XP / TROOP / HOUR", IsSelected: false, CanSelect: true, ...portraitFields("derthert_portrait", ASSETS.derthert) },
      { Name: "Abagai", Detail: "Leadership 121 · Bow 191", Rate: "52 XP / TROOP / HOUR", IsSelected: false, CanSelect: true, ...portraitFields("abagai_portrait", ASSETS.abagai) },
      { Name: "Lucon", Detail: "Leadership 143 · One-Handed 155", Rate: "49 XP / TROOP / HOUR", IsSelected: false, CanSelect: true, ...portraitFields("lucon_portrait", ASSETS.lucon) }
    ],
    Troops: [
      { Name: "Imperial Elite Cataphract", TierText: "TIER 6", CountText: "18 TROOPS", ProgressText: "READY TO UPGRADE", StateText: "XP CAPPED", CanGainXp: false },
      { Name: "Battanian Trained Spearman", TierText: "TIER 3", CountText: "46 TROOPS", ProgressText: "612 / 900 XP", StateText: "RECEIVING TRAINING", CanGainXp: true },
      { Name: "Vlandian Hardened Crossbowman of the Western Marches", TierText: "TIER 4", CountText: "27 TROOPS", ProgressText: "1,018 / 1,300 XP", StateText: "RECEIVING TRAINING", CanGainXp: true },
      { Name: "Khuzait Nomad", TierText: "TIER 1", CountText: "53 TROOPS", ProgressText: "144 / 300 XP", StateText: "RECEIVING TRAINING", CanGainXp: true },
      { Name: "Aserai Veteran Infantry", TierText: "TIER 5", CountText: "22 TROOPS", ProgressText: "1,700 / 1,700 XP", StateText: "XP CAPPED", CanGainXp: false },
      { Name: "Sturgian Woodsman", TierText: "TIER 2", CountText: "31 TROOPS", ProgressText: "210 / 550 XP", StateText: "RECEIVING TRAINING", CanGainXp: true },
      { Name: "Imperial Recruit", TierText: "TIER 1", CountText: "19 TROOPS", ProgressText: "88 / 300 XP", StateText: "RECEIVING TRAINING", CanGainXp: true },
      { Name: "Battanian Fian", TierText: "TIER 5", CountText: "12 TROOPS", ProgressText: "1,211 / 1,700 XP", StateText: "RECEIVING TRAINING", CanGainXp: true },
      { Name: "Vlandian Footman", TierText: "TIER 2", CountText: "38 TROOPS", ProgressText: "447 / 550 XP", StateText: "RECEIVING TRAINING", CanGainXp: true },
      { Name: "Khuzait Horse Archer", TierText: "TIER 4", CountText: "21 TROOPS", ProgressText: "908 / 1,300 XP", StateText: "RECEIVING TRAINING", CanGainXp: true },
      { Name: "Aserai Mameluke Cavalry", TierText: "TIER 5", CountText: "14 TROOPS", ProgressText: "1,350 / 1,700 XP", StateText: "RECEIVING TRAINING", CanGainXp: true },
      { Name: "Sturgian Heroic Line Breaker", TierText: "TIER 5", CountText: "9 TROOPS", ProgressText: "1,699 / 1,700 XP", StateText: "RECEIVING TRAINING", CanGainXp: true }
    ]
  },
  "ReignNotableGenerationPopup.xml": {
    Title: "PREPARING CALRADIA",
    Detail: "Generating the notable characters and relationships needed for this campaign.",
    Progress: "Building notable 18 of 42",
    HasError: false
  },
  "ReignCastleLayoutScreen.xml": {
    CastleMapImageId: "reigneventart|castle_map|empire",
    GovernmentButtonText: "GOVERNMENT — RULER ACCESS",
    GuestBedroomRow1: emptyCastleSlots(5),
    GuestBedroomRow2: emptyCastleSlots(5),
    GuestBedroomRow3: emptyCastleSlots(5),
    GuestBedroomRow4: emptyCastleSlots(5)
  },
  "ReignGovernmentScreen.xml": {
    Title: "SOUTHERN EMPIRE GOVERNMENT",
    ModeText: "RULER WORKING SESSION",
    InstitutionText: "Imperial Senate",
    AuthorityText: "AUTHORITY 4 / 5",
    TrustText: "TRUST 63 / 100",
    StanceText: "RULER STANCE -12  •  Common Weal Coalition",
    MeetingText: "NEXT SEASONAL MEETING: DAY 211.0",
    StatusText: "The senate is debating relief for a recently raided village.",
    LobbyTerms: "Fund the western granary before day 218",
    IsInteractive: true,
    IsReadOnly: false,
    HasSelectedResolution: true,
    HasSelectedMember: true,
    HasActionPressure: true,
    CanOverrideActionPressure: true,
    ActionPressureText: "War — 38% support — approval or override required",
    SelectedResolutionTitle: "Relief for the Raided Villages",
    SelectedResolutionDetail: "Settlement relief — evidence: village_raided\n\nROUTE I: Reduce food dues for one season\nROUTE II: Make a one-time granary donation",
    SelectedMemberText: "Senator Oros — Common Weal Coalition",
    MemberCount: 2,
    Parties: [
      { Name: "Common Weal Coalition", Planks: "PopularWelfare  •  Trade  •  Infrastructure", Speaker: "Speaker: Senator Oros  •  Seats: 9", Alignment: "LEADING", Statement: "Relief must reach the western villages before winter." },
      { Name: "Crown and Legion", Planks: "RoyalAuthority  •  MilitaryStrength  •  Justice", Speaker: "Speaker: Senator Vala  •  Seats: 6", Alignment: "COALITION", Statement: "The throne may act, but the frontier must not be stripped." },
      { Name: "Free Estates", Planks: "LocalAutonomy  •  Trade  •  RepresentativeAuthority", Speaker: "Speaker: Senator Nemos  •  Seats: 4", Alignment: "OPPOSITION", Statement: "Any levy must be bounded and recorded." }
    ],
    Resolutions: [
      { Title: "Relief for the Raided Villages", Summary: "DEBATE  •  Party common_weal", FullDetail: "Two trackable routes", IsSelected: true },
      { Title: "Reinforce the Western Garrison", Summary: "ACTIVE  •  Party crown_legion", FullDetail: "Progress 114 / 160", IsSelected: false }
    ],
    Members: [
      { Name: "Senator Oros", Detail: "Common Weal Coalition  •  Party loyalty 76  •  Government loyalty 68  •  Ruler relation 14", IsSelected: true },
      { Name: "Senator Vala", Detail: "Crown and Legion  •  Party loyalty 81  •  Government loyalty 54  •  Ruler relation -8", IsSelected: false }
    ]
  },
  "ReignCourtScreen.xml": {
    DocketTitle: "ROYAL DOCKET",
    LocalModeNoticeVisible: false,
    RoyalCommandsVisible: true,
    HasChancellor: true,
    ChancellorName: "Lady Adalindis",
    ChancellorPortraitCacheKey: "preview_chancellor_adalindis",
    ChancellorStateText: "INACTIVE • ACTIVE NEXT 8 AM",
    ChancellorSalaryText: "500 denars per Active day",
    ChancellorToggleVisible: true,
    DocketItemCount: 2,
    DocketScrollVisible: false,
    DailyAgenda: [
      {
        Title: "Guildmaster Eronys",
        Subtitle: "A grain shortage is reducing hearth recovery at Ortysia's dependent village.",
        Status: "SERIOUS",
        Meta: "30 food • 7 days",
        IconSprite: "reign_court_docket_accounts"
      },
      {
        Title: "Headman Mengus",
        Subtitle: "Raiding has left Marunath's approaches unsafe for villagers and caravans.",
        Status: "SEVERE",
        Meta: "24 soldiers • 10 days",
        IconSprite: "reign_court_docket_military"
      }
    ]
  },
  "ReignCourtPetitionScreen.xml": {
    Title: "COURT PETITION",
    PetitionerName: "Mengus fen Gruffendoc",
    PetitionerRole: "Petitioner for Ortysia",
    SeverityText: "SERIOUS NEED",
    ProblemText: "The dependent villages cannot replace seed grain lost during the last raids.",
    RequestTypeText: "FOOD RELIEF",
    RequestedResourceText: "30 food from the capital stores for 7 days",
    PurposeText: "Ortysia",
    NeedFacts: "Observed 18.4 • healthy reference 42.0 • normalized need 56%",
    RequestTerms: "30 food from the capital stores for 7 days",
    BenefitText: "If granted: +4 petitioner relation, +1 associated notable relation, and +0.35 hearth growth per day through the commitment.",
    DangerText: "",
    WarningText: "The capital retains a positive food reserve after this grant.",
    EventImageId: "preview_ruler_petition_vlandia_throne_room",
    EventImageAsset: ASSETS.rulerPetitionCourt,
    PortraitCacheKey: "preview_petitioner_eronys",
    PortraitId: "preview_petitioner_native",
    PortraitAsset: ASSETS.mengus,
    PortraitAdditionalArgs: "",
    PortraitTextureProviderName: "CharacterImageTextureProvider",
    HasPortrait: true,
    ActiveParticipants: [
      { Name: "Mengus fen Gruffendoc", Subtitle: "Petitioner for Ortysia", IsActive: true, ...portraitFields("mengus_portrait", ASSETS.mengus, "Active") },
      { Name: "Aevonos Aurelorides", Subtitle: "Court noble", IsActive: true, ...portraitFields("aevonos_portrait", ASSETS.lucon, "Active") }
    ],
    AvailableAttendees: [
      { Name: "Empress Rhagaea Pethros", Subtitle: "Court noble", IsActive: false, ...portraitFields("rhagaea_portrait", ASSETS.rhagaea, "Available") },
      { Name: "Corein fen Gruffendoc", Subtitle: "Court noble", IsActive: false, ...portraitFields("corein_portrait", ASSETS.corein, "Available") },
      { Name: "Abagai of the Khergit Borderlands", Subtitle: "Court guest", IsActive: false, ...portraitFields("abagai_portrait", ASSETS.abagai, "Available") }
    ],
    DirectGrantVisible: true,
    GoldGrantVisible: true,
    CanGrantDirect: true,
    CanGrantWithGold: true,
    CanRefuse: true,
    DecisionComplete: false,
    CanContinue: false,
    DirectGrantLabel: "GRANT FOOD",
    GoldGrantLabel: "FUND INSTEAD • 1,240 DENARS",
    RefuseLabel: "REFUSE REQUEST",
    StatusText: "Guildmaster Eronys awaits your immediate answer.",
    PostponeVisible: true,
    CanPostpone: true,
    Transcript: [
      { Speaker: "Guildmaster Eronys", Text: "My liege, our stores cannot carry the villages through the next planting. We ask for relief now.", Role: "npc", IsNpcLine: true, IsPlayerLine: false, IsSystemLine: false },
      { Speaker: "You", Text: "How quickly will this reach the villages?", Role: "player", IsNpcLine: false, IsPlayerLine: true, IsSystemLine: false },
      { Speaker: "Guildmaster Eronys", Text: "The first carts can leave today, and the full allotment will be distributed under the town reeve's seal.", Role: "npc", IsNpcLine: true, IsPlayerLine: false, IsSystemLine: false }
    ],
    InputText: "",
    ChatScrollVersion: 3,
    CanSend: false,
    ConversationEnabled: true,
    DecisionControlsVisible: true
  },
  "ReignUiCalibrationOverlay.xml": {
    IsLauncherVisible: true,
    IsExpanded: true,
    SelectionVisible: true,
    SelectionX: 525,
    SelectionY: 132,
    SelectionWidth: 610,
    SelectionHeight: 565,
    MovieText: "ReignRoyalCouncilScreen",
    SelectedText: "CouncilTranscriptPanel",
    GeometryText: "X 525 · Y 132 · W 610 · H 565",
    ModeText: "MOVE MODE",
    StatusText: "Select, move, resize, export, and compare against the browser preview."
  },
  "ReignRoyalCouncilScreen.xml": {
    WarSeat: {
      Role: "war",
      Title: "WAR COUNCILOR",
      Name: "Gwydan",
      Status: "AVAILABLE",
      IsAvailable: true,
      HasPortrait: true,
      PortraitCacheKey: "preview_royal_council_war",
      PortraitId: "mengus_portrait",
      PortraitAsset: ASSETS.mengus,
      PortraitAdditionalArgs: "",
      PortraitTextureProviderName: "ReignPortraitProvider"
    },
    SpymasterSeat: {
      Role: "spymaster",
      Title: "SPYMASTER",
      Name: "Gwyin",
      Status: "AVAILABLE",
      IsAvailable: true,
      HasPortrait: true,
      PortraitCacheKey: "preview_royal_council_spymaster",
      PortraitId: "spymaster_portrait",
      PortraitAsset: ASSETS.abagai,
      PortraitAdditionalArgs: "",
      PortraitTextureProviderName: "ReignPortraitProvider"
    },
    EconomicSeat: {
      Role: "economic",
      Title: "ECONOMIC ADVISOR",
      Name: "Elideth",
      Status: "AVAILABLE",
      IsAvailable: true,
      HasPortrait: true,
      PortraitCacheKey: "preview_royal_council_economic",
      PortraitId: "adalindis_portrait",
      PortraitAsset: ASSETS.adalindis,
      PortraitAdditionalArgs: "",
      PortraitTextureProviderName: "ReignPortraitProvider"
    },
    ForeignSeat: {
      Role: "foreign",
      Title: "FOREIGN ADVISOR",
      Name: "Branara",
      Status: "AVAILABLE",
      IsAvailable: true,
      HasPortrait: true,
      PortraitCacheKey: "preview_royal_council_foreign",
      PortraitId: "corein_portrait",
      PortraitAsset: ASSETS.corein,
      PortraitAdditionalArgs: "",
      PortraitTextureProviderName: "ReignPortraitProvider"
    },
    Transcript: [
      { Speaker: "Gwydan · War Councilor", Text: "No wars threaten the realm. Our field forces remain assembled, and the only recent fighting involved bandits beyond the royal host." },
      { Speaker: "Gwyin · Spymaster", Text: "No intelligence operation is active and no agent is exposed. I have no substantiated rumor requiring Your Majesty's attention." },
      { Speaker: "Elideth · Economic Advisor", Text: "The treasury is stable, supplies are adequate, and no settlement reports a material shortage. Current shipments remain on schedule." },
      { Speaker: "Branara · Foreign Advisor", Text: "Relations with neighboring rulers are steady and political pressure remains low. No foreign conflict presently requires a royal response." }
    ],
    InputText: "",
    CanSend: true,
    IsBusy: false,
    StatusText: "The council awaits your question."
  },
  "ReignWarCouncilScreen.xml": {
    MapWidth: 820,
    MapHeight: 820,
    MapOffsetX: 0,
    MapOffsetY: 0,
    MarkerRevision: 12,
    SelectedLordIndex: 0,
    LordCount: 4,
    StatusText: "I \u2022 Bevanoc selected. Use the raven to send orders or other instructions.",
    MapImageId: "reigneventart|war_council|map",
    OuterFrameImageId: "reigneventart|war_council|outer_frame",
    RavenImageId: "reigneventart|war_council|raven_scroll",
    PanelFrameImageId: "reigneventart|war_council|panel_frame",
    TallPanelFrameImageId: "reigneventart|war_council|panel_frame_tall",
    PanelFrameOverlayImageId: "reigneventart|war_council|panel_frame_overlay",
    CouncilorName: "Branara",
    CouncilorTitle: "Independent War Councilor",
    CouncilorSkillText: "Tactics 172 \u2022 range 328\nLeadership 141 \u2022 foreign detection 62%\nCapital \u2022 Zeonica",
    CouncilorPortraitId: "corein_portrait",
    CouncilorPortraitArgs: "",
    CouncilorPortraitProvider: "ReignPortraitProvider",
    CouncilorDropdownOpen: false,
    MessengerVisible: false,
    MessengerTitle: "RAVEN TO BEVANOC",
    MessengerInputText: "Hold the western crossing until relieved.",
    MessengerStatus: "",
    Kingdoms: [
      { Name: "Aserai", Strength: "6,657", Detail: "26 clans \u2022 35 parties \u2022 57 settlements", Relation: "PEACE" },
      { Name: "Vlandia", Strength: "6,364", Detail: "27 clans \u2022 35 parties \u2022 52 settlements", Relation: "WAR" },
      { Name: "Northern Empire", Strength: "5,754", Detail: "24 clans \u2022 31 parties \u2022 47 settlements", Relation: "PEACE" },
      { Name: "Battania", Strength: "5,188", Detail: "18 clans \u2022 28 parties \u2022 42 settlements", Relation: "ALLY" }
    ],
    Reports: [
      { Title: "BEVANOC'S PARTY VS WESTERN ARMY", Result: "Victory \u2022 114–76 troops \u2022 losses 8/31", Captures: "Lord Aldric captured", PlayerRealmInvolved: true, OrdinaryBattle: false },
      { Title: "CARAVAN OF SARGOT VS LOOTERS", Result: "Defeat \u2022 24–38 troops \u2022 losses 24/11", Captures: "No lord captured", PlayerRealmInvolved: false, OrdinaryBattle: true },
      { Title: "BRANARA'S PARTY VS DESERTERS", Result: "Victory \u2022 83–53 troops \u2022 losses 9/33", Captures: "No lord captured", PlayerRealmInvolved: true, OrdinaryBattle: false }
    ],
    Settlements: [
      { TypeLabel: "TOWN", Name: "Amitatys" },
      { TypeLabel: "CASTLE", Name: "Ataconia Castle" },
      { TypeLabel: "VILLAGE", Name: "Ayn Assadi" },
      { TypeLabel: "TOWN", Name: "Baltakhand" },
      { TypeLabel: "CASTLE", Name: "Chanopsis Castle" },
      { TypeLabel: "VILLAGE", Name: "Dvorusta" },
      { TypeLabel: "TOWN", Name: "Epicrotea" },
      { TypeLabel: "TOWN", Name: "Zeonica" }
    ],
    Lords: [
      { Roman: "I", Name: "Bevanoc", ClanName: "Caerder", PartyText: "133 \u2022 I 19  M 114  C 0", StatusText: "Following Orders", PortraitId: "mengus_portrait", PortraitArgs: "", PortraitProvider: "ReignPortraitProvider", RavenImageId: "reigneventart|war_council|raven_scroll", CanMobilize: false, IsSelected: true },
      { Roman: "II", Name: "Bevanwyn", ClanName: "Caerder", PartyText: "166 \u2022 I 33  M 120  C 13", StatusText: "Following Orders", PortraitId: "aeron_portrait", PortraitArgs: "", PortraitProvider: "ReignPortraitProvider", RavenImageId: "reigneventart|war_council|raven_scroll", CanMobilize: false, IsSelected: false },
      { Roman: "III", Name: "Branara", ClanName: "Caerder", PartyText: "124 \u2022 I 12  M 110  C 2", StatusText: "Free Roam", PortraitId: "corein_portrait", PortraitArgs: "", PortraitProvider: "ReignPortraitProvider", RavenImageId: "reigneventart|war_council|raven_scroll", CanMobilize: false, IsSelected: false },
      { Roman: "—", Name: "Llewara", ClanName: "Caerder", PartyText: "No active party", StatusText: "Free Roam", PortraitId: "ira_portrait", PortraitArgs: "", PortraitProvider: "ReignPortraitProvider", RavenImageId: "reigneventart|war_council|raven_scroll", CanMobilize: true, IsSelected: false }
    ],
    CouncilorCandidates: [
      { Name: "Bevanoc", CouncilorDetail: "Tactics 119 \u2022 Leadership 134", PortraitId: "mengus_portrait", PortraitArgs: "", PortraitProvider: "ReignPortraitProvider", IsCouncilor: false },
      { Name: "Branara", CouncilorDetail: "Tactics 172 \u2022 Leadership 141", PortraitId: "corein_portrait", PortraitArgs: "", PortraitProvider: "ReignPortraitProvider", IsCouncilor: true },
      { Name: "Llewara", CouncilorDetail: "Tactics 86 \u2022 Leadership 109", PortraitId: "ira_portrait", PortraitArgs: "", PortraitProvider: "ReignPortraitProvider", IsCouncilor: false }
    ]
  },
  "ReignSpymasterScreen.xml": {
    SpymasterName: "Gwyin",
    PortraitCacheKey: "preview_spymaster_abagai",
    PortraitId: "spymaster_portrait",
    PortraitAsset: ASSETS.spymasterFullBody,
    PortraitArgs: "",
    PortraitProvider: "ReignPortraitProvider",
    HasPortrait: true,
    HasSpymaster: true,
    ShowSpymasterAiPortrait: true,
    ShowSpymasterTableauFallback: false,
    ShowAppointmentPrompt: false,
    AppointmentPending: false,
    SpymasterModel: {
      HasSpymaster: true,
      BannerCodeText: "",
      BodyProperties: "",
      CharStringId: "lord_6_12",
      EquipmentCode: "",
      IsFemale: true,
      MountCreationKey: "",
      StanceIndex: 0,
      ArmorColor1: "#473421FF",
      ArmorColor2: "#8C733DFF",
      Race: 0,
      IsTableauEnabled: false
    },
    IsIntelligence: true,
    IsSubterfuge: false,
    IsReputation: false,
    IsArchive: false,
    ShowOperationPanel: true,
    IsLands: true,
    IsPeople: false,
    ShowPeopleGroups: false,
    ShowSocialItem: false,
    PeopleGroupText: "OWN NOBLES",
    SocialScopeText: "YOUR REPUTATION",
    TargetName: "Ab Comer Castle",
    TargetDetail: "A stronghold held by foreign forces.",
    ActionName: "Gather settlement intelligence",
    OperationDetail: "Appoint a Spymaster and choose an operation.",
    AssessmentText: "UNKNOWN",
    IsOrdinaryAssessment: true,
    IsChallengingAssessment: false,
    SocialItemName: "Select rumor or reputation",
    TargetDropdownOpen: false,
    ActionDropdownOpen: false,
    SocialDropdownOpen: false,
    CanBegin: false,
    CanBreakout: false,
    BreakoutText: "",
    StatusText: "",
    ReportText: "No intelligence operations have been recorded.",
    TargetOptions: [
      { Id: "castle_A1", Label: "Ab Comer Castle", Detail: "A stronghold held by foreign forces.", IsSelected: true },
      { Id: "town_V6", Label: "Pravend", Detail: "A stronghold within your realm.", IsSelected: false },
      { Id: "town_K1", Label: "Marunath", Detail: "A stronghold held by foreign forces.", IsSelected: false }
    ],
    ActionOptions: [
      { Id: "land_intelligence", Label: "Gather settlement intelligence", Detail: "", IsSelected: true },
      { Id: "counterintelligence", Label: "Counterintelligence sweep", Detail: "", IsSelected: false }
    ],
    SocialItems: [],
    Reports: []
  },
  "ReignCourtEconomicReportScreen.xml": {
    SourceName: "Pravend",
    TargetName: "Sargot",
    GoodsName: "FOOD SUPPLY",
    AvailableText: "600",
    AmountText: "100",
    ArrivalText: "ARRIVES IN: 2.4 DAYS",
    Notes: "Winter relief for the western garrison.",
    IsSourceDropdownOpen: false,
    IsTargetDropdownOpen: true,
    SourceOptions: [
      { StringId: "town_V6", Name: "Pravend", DetailText: "Town - Vlandia" },
      { StringId: "town_V1", Name: "Sargot", DetailText: "Town - Vlandia" },
      { StringId: "town_V4", Name: "Jaculan", DetailText: "Town - Vlandia" }
    ],
    TargetOptions: [
      { StringId: "town_V6", Name: "Pravend", DetailText: "Town - Vlandia" },
      { StringId: "town_V1", Name: "Sargot", DetailText: "Town - Vlandia" },
      { StringId: "town_A1", Name: "Ortysia", DetailText: "Town - Western Empire" },
      { StringId: "castle_V2", Name: "Talivel Castle", DetailText: "Castle - Vlandia" },
      { StringId: "town_K1", Name: "Marunath", DetailText: "Town - Battania" },
      { StringId: "town_B1", Name: "Car Banseth", DetailText: "Town - Battania" },
      { StringId: "town_S1", Name: "Varnovapol", DetailText: "Town - Sturgia" }
    ],
    ReportDateText: "16th of Summer, 1092",
    StatusText: "",
    TreasuryText: "38,742",
    DailyIncomeText: "+4,582",
    DailyExpensesText: "-3,271",
    NetDailyChangeText: "+1,311",
    TariffsTradeText: "1,842",
    VillageManorText: "2,154",
    TradeBalanceText: "-1,126",
    OutstandingDebtsText: "2,390",
    GrainTotalText: "14,809",
    MeatTotalText: "6,320",
    TimberTotalText: "8,700",
    IronTotalText: "4,210",
    TotalMilitiaText: "1,380",
    TotalGarrisonText: "920",
    GarrisonUpkeepText: "1,682",
    WarInventoryText: "Adequate",
    OverallStabilityText: "73",
    AverageLoyaltyText: "73",
    AverageSecurityText: "66",
    CorruptionRiskText: "Low",
    NetDailyPositive: true,
    NetDailyNegative: false,
    TradeBalancePositive: false,
    TradeBalanceNegative: true,
    CanSubmit: true,
    HasSettlementOverflow: true,
    EconomicAdvisor: {
      Role: "economic",
      Title: "ECONOMIC ADVISOR",
      Name: "Elideth",
      Status: "AVAILABLE",
      IsAvailable: true,
      HasPortrait: true,
      PortraitCacheKey: "preview_economic_report_advisor",
      PortraitId: "adalindis_portrait",
      PortraitAsset: ASSETS.adalindis,
      PortraitAdditionalArgs: "",
      PortraitTextureProviderName: "ReignPortraitProvider"
    },
    Settlements: [
      { Name: "Pravend", GovernorName: "Eadfrid of the Oak", ProsperityText: "78", FoodText: "82", SecurityText: "76", LoyaltyText: "88", MilitiaText: "330", GarrisonText: "210", GarrisonFoodText: "120", GarrisonWageText: "420", GrainText: "1,210", FishText: "680", MeatText: "420", OlivesText: "360", BeerText: "240", ButterText: "220", GrapesText: "310", DatesText: "180" },
      { Name: "Sargot", GovernorName: "Lanthos of Vostrum", ProsperityText: "64", FoodText: "58", SecurityText: "62", LoyaltyText: "70", MilitiaText: "270", GarrisonText: "180", GarrisonFoodText: "100", GarrisonWageText: "360", GrainText: "1,110", FishText: "410", MeatText: "380", OlivesText: "320", BeerText: "200", ButterText: "190", GrapesText: "280", DatesText: "150" },
      { Name: "Ocs Hall", GovernorName: "Dionor of Ocs", ProsperityText: "72", FoodText: "74", SecurityText: "70", LoyaltyText: "75", MilitiaText: "290", GarrisonText: "190", GarrisonFoodText: "110", GarrisonWageText: "400", GrainText: "1,260", FishText: "560", MeatText: "330", OlivesText: "330", BeerText: "210", ButterText: "200", GrapesText: "330", DatesText: "170" },
      { Name: "Jaculan", GovernorName: "Meritor of Jaculan", ProsperityText: "56", FoodText: "65", SecurityText: "50", LoyaltyText: "60", MilitiaText: "290", GarrisonText: "160", GarrisonFoodText: "110", GarrisonWageText: "320", GrainText: "960", FishText: "330", MeatText: "340", OlivesText: "240", BeerText: "160", ButterText: "160", GrapesText: "260", DatesText: "120" },
      { Name: "Charas", GovernorName: "Belithor of Charas", ProsperityText: "68", FoodText: "71", SecurityText: "74", LoyaltyText: "68", MilitiaText: "260", GarrisonText: "170", GarrisonFoodText: "110", GarrisonWageText: "340", GrainText: "1,090", FishText: "440", MeatText: "280", OlivesText: "260", BeerText: "180", ButterText: "170", GrapesText: "240", DatesText: "140" }
    ],
    SurplusRows: [
      { SettlementName: "Pravend", ItemName: "Grain", AmountText: "+1,210", IsSurplus: true, IsShortage: false },
      { SettlementName: "Ocs Hall", ItemName: "Fish", AmountText: "+560", IsSurplus: true, IsShortage: false },
      { SettlementName: "Charas", ItemName: "Olives", AmountText: "+260", IsSurplus: true, IsShortage: false },
      { SettlementName: "Sargot", ItemName: "Meat", AmountText: "-380", IsSurplus: false, IsShortage: true },
      { SettlementName: "Jaculan", ItemName: "Grain", AmountText: "-960", IsSurplus: false, IsShortage: true }
    ]
  },
  "ReignPartyChatScreen.xml": {
    IsHomesMode: false, IsHomeDirectory: false, HasChatSession: true, AllowGroupSelection: true,
    RosterTitle: "Available Characters", ClearSelectionText: "Clear", HomeDirectoryText: "", InputEnabled: true,
    Title: "Council Before the Lords of Calradia",
    LocationText: "Great Hall of Amitatys",
    SelectedSummary: "3 characters selected · 2 active · 7 available",
    BusyText: "",
    PartyMembers: SAMPLE_DATA.PartyMembers.map((member, index) => ({
      ...member,
      CanVisitInPerson: false, DisplayStatus: member.Status, StatusTop: 65, StatusHeight: 28,
      IsActive: index < 2,
      IsInactive: index >= 2
    })),
    ChatLines: [
      {
        Speaker: "Mengus fen Gruffendoc",
        Text: "The western clans will support the grain road, but only if every promised banner is named before the council adjourns.",
        IsPlayerLine: false,
        IsNpcLine: true,
        IsSystemLine: false
      },
      {
        Speaker: "Aeric fen Seanel",
        Text: "Then record my oath first. My riders will hold the western pass until the final wagon reaches Marunath.",
        IsPlayerLine: true,
        IsNpcLine: false,
        IsSystemLine: false
      },
      {
        Speaker: "Council Herald",
        Text: "Corein, Daughter of High King Caladog, has joined the party conversation.",
        IsPlayerLine: false,
        IsNpcLine: false,
        IsSystemLine: true
      },
      {
        Speaker: "Corein, Daughter of High King Caladog",
        Text: "Long names and longer promises are easy. Tell us how many companies can actually ride before the first snow.",
        IsPlayerLine: false,
        IsNpcLine: true,
        IsSystemLine: false
      }
    ]
  },
  "ReignCastleChatScreen.xml": {
    CastleRoomName: "Castle Gardens",
    HasCastleArt: true,
    CastleArtImageId: "feast_empire_toasts_and_table_talk",
    CastleTavernArtImageId: "reigntavern|scene|empire|tavern_01.jpg",
    ShowCastleTavernArt: false,
    ShowCastleApprovedFallback: false,
    EventImageAsset: ASSETS.eventArt,
    PlayerDisplayName: "Fercread\nRuler",
    PlayerPortraitId: "player_portrait",
    PlayerPortraitAdditionalArgs: "",
    PlayerPortraitTextureProviderName: "ReignPortraitProvider",
    PlayerPortraitCacheKey: "preview_castle_chat_player",
    IsBusy: true,
    BusyText: "Waiting for Ceryniana to speak... (3 of 4)",
    InputEnabled: false,
    PortraitPreviewCacheKey: "preview_castle_chat_portrait",
    PartyMembers: [
      { Name: "Zotyra", HasNativePortrait: true, IsChild: false, ...portraitFields("zotyra_portrait", ASSETS.ira, "Present") },
      { Name: "Othessa", HasNativePortrait: true, IsChild: false, ...portraitFields("othessa_portrait", ASSETS.corein, "Present") },
      { Name: "Ceryniana", HasNativePortrait: true, IsChild: false, ...portraitFields("ceryniana_portrait", ASSETS.adalindis, "Speaking") },
      { Name: "Seorgys", HasNativePortrait: true, IsChild: false, ...portraitFields("seorgys_portrait", ASSETS.mengus, "Waiting") }
    ],
    ChatLines: [
      {
        Speaker: "Zotyra",
        Text: "The roses have taken well to the southern wall, though the household still argues over who deserves the credit.",
        IsPlayerLine: false,
        IsNpcLine: true,
        IsSystemLine: false
      },
      {
        Speaker: "Othessa",
        Text: "Credit matters less than whether the garden remains a place where difficult truths can be spoken without an audience gathering at the doors.",
        IsPlayerLine: false,
        IsNpcLine: true,
        IsSystemLine: false
      }
    ]
  },
  "ReignIndividualChatScreen.xml": {
    ChatLines: INDIVIDUAL_CHAT_LINES,
    LocationText: "Location: Marunath",
    BusyText: "",
    PlayerPortraitCropImageWidth: 241.8,
    PlayerPortraitCropImageHeight: 322.4,
    NpcPortraitCropImageWidth: 241.8,
    NpcPortraitCropImageHeight: 322.4,
    ZoomPortraitImageWidth: 540,
    ZoomPortraitImageHeight: 720,
    ZoomFrameWidth: 576,
    ZoomFrameHeight: 756
  },
  "ReignCorrespondenceScreen.xml": {
    ChatLines: [
      {
        Speaker: "Mengus fen Gruffendoc · Day 184 (delivered)",
        Text: "The western clans have accepted your proposed winter grain escort, but they expect proof that every promised banner will actually arrive.",
        IsPlayerLine: false,
        IsNpcLine: true,
        IsSystemLine: false
      },
      {
        Speaker: "Aeric fen Seanel · Day 185 (in transit)",
        Text: "Then tell them the first wagons leave within seven days. I will ride with the escort until it clears the western pass.",
        IsPlayerLine: true,
        IsNpcLine: false,
        IsSystemLine: false
      },
      {
        Speaker: "Mengus fen Gruffendoc · Day 186 (read)",
        Text: "That answer will satisfy most of them. Caladog will still demand names, numbers, and the road each company is sworn to defend.",
        IsPlayerLine: false,
        IsNpcLine: true,
        IsSystemLine: false
      }
    ],
    BusyText: ""
  }
};

const PREFAB_ROOT_KEYS = {
  "ReignTavernHouseScreen.xml": TAVERN_HOUSE_ROOT_KEYS,
  "ReignClanAccordsScreen.xml": CLAN_ACCORDS_ROOT_KEYS,
  "AIPortraitsMemoriesBook.xml": ["MemoryItems", "MemoryBookImageId", "TitleText", "CaptionText", "CounterText", "HasMemories"],
  "ReignAmbassadorScreen.xml": ["PlayerBannerId", "PlayerBannerAsset", "PlayerBannerArgs", "PlayerBannerProvider", "ForeignAdvisor", "Ambassadors", "ShowEmptyState", "StatusText"],
  "ReignCastleChatScreen.xml": ["CastleRoomName", "HasCastleArt", "CastleArtImageId", "CastleTavernArtImageId", "ShowCastleTavernArt", "ShowCastleApprovedFallback", "EventImageAsset", "PlayerDisplayName", "PlayerPortraitId", "PlayerPortraitAdditionalArgs", "PlayerPortraitTextureProviderName", "PlayerPortraitCacheKey", "IsBusy", "BusyText", "InputEnabled", "InputText", "PartyMembers", "ChatLines", "ChatScrollVersion", "IsOverlayVisible", "IsPortraitPreviewVisible", "PortraitPreviewId", "PortraitPreviewAsset", "PortraitPreviewAdditionalArgs", "PortraitPreviewTextureProviderName", "PortraitPreviewCacheKey", "PortraitPreviewName"],
  "ReignCastleLayoutScreen.xml": ["CastleMapImageId", "GovernmentButtonText", "CastleGardenSlots", "TrainingYardSlots", "StableCourtyardSlots", "GuestBedroomRow1", "GuestBedroomRow2", "GuestBedroomRow3", "GuestBedroomRow4", "NobleSolarSlots", "InnerCourtyardSlots", "MainHallSlots", "RoyalBedroomSlots", "ThroneRoomSlots", "LibrarySlots", "DiningChamberSlots", "PortraitGallerySlots", "ChapelSlots", "BathsSlots", "BattlementSlots"],
  "ReignCorrespondenceScreen.xml": ["Contacts", "SelectedName", "SelectedSubtitle", "ChatLines", "ChatScrollVersion", "BusyText", "InputText"],
  "ReignCourtEconomicReportScreen.xml": ["SourceName", "TargetName", "GoodsName", "AvailableText", "AmountText", "ArrivalText", "Notes", "IsSourceDropdownOpen", "IsTargetDropdownOpen", "SourceOptions", "TargetOptions", "ReportDateText", "StatusText", "TreasuryText", "DailyIncomeText", "DailyExpensesText", "NetDailyChangeText", "TariffsTradeText", "VillageManorText", "TradeBalanceText", "OutstandingDebtsText", "GrainTotalText", "MeatTotalText", "TimberTotalText", "IronTotalText", "TotalMilitiaText", "TotalGarrisonText", "GarrisonUpkeepText", "WarInventoryText", "NetDailyPositive", "NetDailyNegative", "TradeBalancePositive", "TradeBalanceNegative", "CanSubmit", "HasSettlementOverflow", "EconomicAdvisor", "Settlements", "SurplusRows"],
  "ReignCourtScreen.xml": ["CourtTitle", "DocketTitle", "PlayerName", "PlayerBannerId", "PlayerBannerAsset", "PlayerBannerArgs", "PlayerBannerProvider", "HomeVisible", "LargeHomeVisible", "ReferenceHomeVisible", "TabContentVisible", "DetailVisible", "AudienceVisible", "ActionsVisible", "ScreenTitle", "ScreenSubtitle", "StatusText", "DetailTitle", "DetailBody", "DetailMeta", "AudienceSpeaker", "AudienceText", "AudienceEmotion", "AudienceInput", "AudienceHasPortrait", "AudiencePortraitId", "AudiencePortraitAsset", "AudiencePortraitAdditionalArgs", "AudiencePortraitTextureProviderName", "AudiencePortraitCacheKey", "Tabs", "Counters", "Lands", "DailyAgenda", "DocketItemCount", "DocketScrollVisible", "People", "DiplomaticStatus", "AmbassadorCards", "Rumors", "TabRows", "Actions", "RoyalCommandsVisible", "LocalModeNoticeVisible", "HasChancellor", "ChancellorName", "ChancellorPortraitCacheKey", "ChancellorStateText", "ChancellorSalaryText", "ChancellorToggleVisible"],
  "ReignCourtPetitionScreen.xml": ["Title", "PetitionerName", "PetitionerRole", "SeverityText", "ProblemText", "RequestTypeText", "RequestedResourceText", "PurposeText", "NeedFacts", "RequestTerms", "BenefitText", "DangerText", "WarningText", "EventImageId", "EventImageAsset", "ActiveParticipants", "AvailableAttendees", "DirectGrantVisible", "GoldGrantVisible", "CanGrantDirect", "CanGrantWithGold", "CanRefuse", "DecisionComplete", "DecisionControlsVisible", "PostponeVisible", "CanPostpone", "CanContinue", "DirectGrantLabel", "GoldGrantLabel", "RefuseLabel", "StatusText", "Transcript", "InputText", "ChatScrollVersion", "CanSend", "ConversationEnabled"],
  "ReignDiplomacyAnnouncementScreen.xml": ["Title", "DateText", "ShowTarget", "Outcome", "Summary", "Terms", "ActorName", "ActorKingdom", "ActorReason", "ActorPortraitId", "ActorPortraitAsset", "ActorPortraitArgs", "ActorPortraitProvider", "ActorPortraitCacheKey", "ActorBannerId", "ActorBannerArgs", "ActorBannerProvider", "TargetName", "TargetKingdom", "TargetReason", "TargetPortraitId", "TargetPortraitAsset", "TargetPortraitArgs", "TargetPortraitProvider", "TargetPortraitCacheKey", "TargetBannerId", "TargetBannerArgs", "TargetBannerProvider"],
  "ReignFamilyChambersScreen.xml": ["Title", "Subtitle", "Adults", "Children", "SelectionText", "StatusText", "IsBusy", "CanEnterScene"],
  "ReignTrainingYardScreen.xml": ["Title", "TrainerHeading", "TroopHeading", "SelectedTrainerName", "SelectedTrainerDetail", "LeadershipText", "WeaponText", "RateText", "ElapsedText", "DeliveredText", "StatusText", "IsTraining", "CanTrain", "CanStop", "HasSelectedTrainer", "HasSelectedGeneratedPortrait", "SelectedPortraitCacheKey", "SelectedPortraitId", "SelectedPortraitAdditionalArgs", "SelectedPortraitTextureProviderName", "PortraitAsset", "TrainerCount", "TroopCount", "Trainers", "Troops"],
  "ReignGovernmentScreen.xml": GOVERNMENT_ROOT_KEYS,
  "ReignIndividualChatScreen.xml": ["PlayerName", "PlayerSubtitle", "PlayerInfo", "PlayerPortraitId", "PlayerPortraitAsset", "PlayerPortraitAdditionalArgs", "PlayerPortraitTextureProviderName", "PlayerPortraitCropImageWidth", "PlayerPortraitCropImageHeight", "NpcName", "NpcSubtitle", "NpcInfo", "NpcPortraitId", "NpcPortraitAsset", "NpcPortraitAdditionalArgs", "NpcPortraitTextureProviderName", "NpcPortraitCropImageWidth", "NpcPortraitCropImageHeight", "LocationText", "IsBusy", "BusyText", "InputEnabled", "InputText", "ChatLines", "ChatScrollVersion", "IsPortraitZoomVisible", "IsPregnancyWarningVisible", "ZoomFrameWidth", "ZoomFrameHeight", "ZoomPortraitImageWidth", "ZoomPortraitImageHeight", "ZoomPortraitId", "ZoomPortraitAsset", "ZoomPortraitAdditionalArgs", "ZoomPortraitTextureProviderName"],
  "ReignNotableGenerationPopup.xml": ["Title", "Detail", "Progress", "HasError"],
  "ReignPartyChatScreen.xml": ["Title", "LocationText", "SelectedSummary", "BusyText", "InputText", "ChatLines", "PartyMembers", "IsOverlayVisible", "IsPortraitPreviewVisible", "PortraitPreviewId", "PortraitPreviewAsset", "PortraitPreviewAdditionalArgs", "PortraitPreviewTextureProviderName", "PortraitPreviewName"],
  "ReignRoyalCouncilScreen.xml": ["WarSeat", "SpymasterSeat", "EconomicSeat", "ForeignSeat", "Transcript", "InputText", "CanSend", "IsBusy", "StatusText"],
  "ReignSocialEventScreen.xml": ["IsOverlayVisible", "ShowPhaseControls", "ShowWildernessArt", "EventTitle", "PhaseTitle", "PhaseStatus", "PhaseDescription", "EventImageId", "EventImageAsset", "EventImageText", "EventArtSize", "EventArtRowHeight", "AttendeeListTopMargin", "AttendeeListBottomMargin", "AttendeeTitleTopMargin", "EventArtTopMargin", "EventBackgroundImageId", "BackgroundImageId", "BackgroundAsset", "BackgroundAdditionalArgs", "BackgroundTextureProviderName", "CultureTint", "BusyText", "InputText", "ChatLines", "ChatScrollVersion", "ActiveParticipants", "AvailableAttendees", "IsPortraitPreviewVisible", "PortraitPreviewFrameWidth", "PortraitPreviewFrameHeight", "PortraitPreviewImageWidth", "PortraitPreviewImageHeight", "PortraitPreviewId", "PortraitPreviewAsset", "PortraitPreviewAdditionalArgs", "PortraitPreviewTextureProviderName", "PortraitPreviewName"],
  "ReignSpymasterScreen.xml": ["SpymasterName", "PortraitCacheKey", "PortraitId", "PortraitArgs", "PortraitProvider", "HasPortrait", "HasSpymaster", "ShowSpymasterAiPortrait", "ShowSpymasterTableauFallback", "ShowAppointmentPrompt", "AppointmentPending", "SpymasterModel", "IsIntelligence", "IsSubterfuge", "IsReputation", "IsArchive", "ShowOperationPanel", "IsLands", "IsPeople", "ShowPeopleGroups", "ShowSocialItem", "PeopleGroupText", "SocialScopeText", "TargetName", "TargetDetail", "ActionName", "OperationDetail", "AssessmentText", "IsOrdinaryAssessment", "IsChallengingAssessment", "SocialItemName", "TargetDropdownOpen", "ActionDropdownOpen", "SocialDropdownOpen", "CanBegin", "CanBreakout", "BreakoutText", "StatusText", "ReportText", "TargetOptions", "ActionOptions", "SocialItems", "Reports"],
  "ReignUiCalibrationOverlay.xml": ["IsLauncherVisible", "IsExpanded", "SelectionVisible", "SelectionX", "SelectionY", "SelectionWidth", "SelectionHeight", "MovieText", "SelectedText", "GeometryText", "ModeText", "StatusText"],
  "ReignWarCouncilScreen.xml": ["MapWidth", "MapHeight", "MapOffsetX", "MapOffsetY", "MarkerRevision", "SelectedLordIndex", "LordCount", "StatusText", "MapImageId", "OuterFrameImageId", "RavenImageId", "PanelFrameImageId", "TallPanelFrameImageId", "PanelFrameOverlayImageId", "CouncilorName", "CouncilorTitle", "CouncilorSkillText", "CouncilorPortraitId", "CouncilorPortraitArgs", "CouncilorPortraitProvider", "CouncilorDropdownOpen", "MessengerVisible", "MessengerTitle", "MessengerInputText", "MessengerStatus", "Kingdoms", "Reports", "Settlements", "Lords", "CouncilorCandidates"]
};

export function getNativeAugmentationSampleData(target) {
  const skillXpHint = {
    HintText: "Current experience and progress toward the next skill level.",
    ReignIsCurrentHeroSkillVisible: true
  };
  const learningLimitTooltip = {
    HintText: "The learning limit derived from attributes and focus.",
    ReignIsSkillValueVisible: true
  };
  const learningRateTooltip = {
    HintText: "The current experience multiplier for this skill.",
    ReignIsSkillValueVisible: true
  };
  const currentSkill = {
    LearningRate: 4.25,
    CurrentSkillXP: 3480,
    XpRequiredForNextLevel: 4200,
    ReignSkillLevelText: "124",
    ReignSkillProgressText: "3,480 / 4,200",
    ReignLearningRateText: "Learning rate 4.25x",
    ReignIsSkillValueVisible: true,
    ReignIsCurrentHeroSkillVisible: true,
    CanLearnSkill: true,
    SkillXPHint: { ...skillXpHint },
    LearningLimitTooltip: { ...learningLimitTooltip },
    LearningRateTooltip: { ...learningRateTooltip }
  };
  const marriageMember = {
    Relation: 18,
    ReignIsRelationVisible: false,
    ReignIsRelationHidden: true
  };
  return {
    IsSelected: true,
    IsAnyValidMemberSelected: true,
    IsReignTavernArtVisible: true,
    ReignTavernSceneImageId: "reigntavern|scene|empire|preview",
    ReignTavernFrameImageId: "reigntavern|frame",
    ReignSkillValueText: "124",
    ReignSkillLevelText: "124",
    ReignSkillProgressText: "3,480 / 4,200",
    ReignLearningRateText: "Learning rate 4.25x",
    ReignSmithySkillLevelText: "124",
    ReignCraftingSkillText: "124",
    ReignRelationText: "?",
    ReignIsRelationVisible: false,
    ReignIsRelationHidden: true,
    ReignIsSkillValueVisible: true,
    ReignIsSkillValueHidden: false,
    ReignIsCurrentHeroSkillVisible: true,
    ReignIsCraftingSkillVisible: true,
    ReignIsCraftingSkillHidden: false,
    CanLearnSkill: true,
    HasSkillValueIncreasedInCurrentStage: true,
    CurrentSkill: currentSkill,
    LearningLimitTooltip: learningLimitTooltip,
    LearningRateTooltip: learningRateTooltip,
    OffereeClanMember: { ...marriageMember },
    OffererClanMember: { ...marriageMember },
    IsReignPortraitAvailable: true,
    ReignPortraitCacheKey: "preview_native_party_portrait",
    PortraitAsset: ASSETS.mengus,
    Banner_9: { AdditionalArgs: "", Id: "player_clan_banner", TextureProviderName: "BannerTableau", BannerAsset: ASSETS.courtPlayerBanner },
    IsConversationPortraitVisible: true,
    IsPlayerPortraitVisible: true,
    IsAIInfluenceMemoryVisible: true,
    IsPortraitZoomVisible: false,
    NpcPortraitCropImageWidth: 188,
    NpcPortraitCropImageHeight: 250.6667,
    ConversationPortraitTextureProviderName: "ReignPortraitProvider",
    ConversationPortraitAdditionalArgs: "",
    ConversationPortraitId: "mengus_portrait",
    ConversationPortraitAsset: ASSETS.mengus,
    PlayerPortraitCropImageWidth: 188,
    PlayerPortraitCropImageHeight: 250.6667,
    PlayerPortraitTextureProviderName: "ReignPortraitProvider",
    PlayerPortraitAdditionalArgs: "",
    PlayerPortraitId: "adalindis_portrait",
    PlayerPortraitAsset: ASSETS.adalindis,
    AIInfluenceMemoryFrameWidth: 746,
    AIInfluenceMemoryFrameHeight: 566,
    AIInfluenceMemoryImageWidth: 720,
    AIInfluenceMemoryImageHeight: 540,
    AIInfluenceMemoryTextureProviderName: "ReignPortraitProvider",
    AIInfluenceMemoryAdditionalArgs: "",
    AIInfluenceMemoryId: "conversation_memory_scene",
    AIInfluenceMemoryAsset: ASSETS.eventArt,
    ZoomFrameWidth: 726,
    ZoomFrameHeight: 956,
    ZoomPortraitImageWidth: 690,
    ZoomPortraitImageHeight: 920,
    ZoomPortraitTextureProviderName: "ReignPortraitProvider",
    ZoomPortraitAdditionalArgs: "",
    ZoomPortraitId: "mengus_portrait",
    ZoomPortraitAsset: ASSETS.mengus,
    $nativeAugmentationTarget: target
  };
}

function courtLifePreview(source, title, participants, reply, terms, severity = "AT COURT", overrides = {}) {
  return {
    Title: `${source} AUDIENCE`, PetitionerName: title, PetitionerRole: source, SeverityText: severity,
    ProblemText: terms, RequestTypeText: source, RequestedResourceText: terms, PurposeText: "A personal audience with the ruler",
    NeedFacts: participants.map((person) => person.Name).join(" • "), RequestTerms: terms, BenefitText: "", DangerText: "", WarningText: "",
    HasPortrait: Boolean(participants[0]?.HasPortrait), PortraitId: participants[0]?.PortraitId || "", PortraitAsset: participants[0]?.PortraitAsset || "",
    ActiveParticipants: participants.map((person) => ({ PortraitAdditionalArgs: "", PortraitTextureProviderName: "ReignPortraitProvider",
      ...person, IsActive: true, Status: person.Subtitle })), AvailableAttendees: [],
    DirectGrantVisible: true, GoldGrantVisible: true, CanGrantDirect: true, CanGrantWithGold: false, CanRefuse: true,
    DecisionComplete: false, CanContinue: false, DirectGrantLabel: "REVIEW DECISIONS", GoldGrantLabel: "CONFIRM", RefuseLabel: "END AUDIENCE",
    StatusText: "Your guests await your response.", PostponeVisible: true, CanPostpone: true, InputText: "Tell me more.", CanSend: true,
    ConversationEnabled: true, DecisionControlsVisible: true, ChatScrollVersion: 2,
    Transcript: [{ Speaker: participants[0].Name, Text: reply, Role: "assistant", IsNpcLine: true, IsPlayerLine: false, IsSystemLine: false },
      { Speaker: "You", Text: "Thank you for coming. Tell me more.", Role: "user", IsNpcLine: false, IsPlayerLine: true, IsSystemLine: false }],
    ...overrides
  };
}

export function getSampleDataForPrefab(fileName, preset = "") {
  const normalizedName = String(fileName || "").replaceAll("\\", "/").split("/").at(-1);
  if (normalizedName === "ReignClanAccordsScreen.xml") return getClanAccordsSampleData(preset);
  if (normalizedName === "ReignGovernmentScreen.xml") return getGovernmentHearingSampleData(preset);
  if (normalizedName === "ReignTavernHouseScreen.xml") return getTavernHouseSampleData(preset);
  const rootKeys = PREFAB_ROOT_KEYS[normalizedName];
  const base = rootKeys
    ? Object.fromEntries(rootKeys.filter((key) => Object.prototype.hasOwnProperty.call(SAMPLE_DATA, key)).map((key) => [key, SAMPLE_DATA[key]]))
    : SAMPLE_DATA;
  const result = {
    ...base,
    ...(PREFAB_SAMPLE_OVERRIDES[normalizedName] || {}),
    ...(PREVIEW_PRESET_OVERRIDES[preset] || {})
  };
  if (normalizedName === "ReignPartyChatScreen.xml" && preset.startsWith("homes-")) {
    const atHome = preset === "homes-private-chat";
    const empty = preset === "homes-empty";
    const people = [
      ["Livia Bellori", "Tavern worker", ASSETS.ira], ["Oren Mercato", "Merchant", ASSETS.mengus],
      ["Beatrice Voss", "Artisan", ASSETS.adalindis], ["Caius Orlandi", "Guard", ASSETS.derthert],
      ["Zara Caldera", "Resident", ASSETS.abagai], ["Tobias Marin", "Barber", ASSETS.aeron],
      ["Hilda Rainault", "Tavern keeper", ASSETS.corein], ["Lucian Ferrano", "Smith", ASSETS.caladog]
    ];
    Object.assign(result, {
      IsHomesMode: true, IsHomeDirectory: !atHome, HasChatSession: atHome, AllowGroupSelection: false,
      Title: "Homes — Zeonica", LocationText: atHome ? "At home with Livia Bellori" : "People you have met here",
      RosterTitle: "Known residents", ClearSelectionText: "Homes", InputEnabled: atHome,
      SelectedSummary: atHome ? "Private visit · only you and Livia Bellori can hear" : `${empty ? 0 : people.length} known residents at home`,
      HomeDirectoryText: empty ? "You have not met anyone living here yet. Speak with people in the settlement to get to know them."
        : "Select a resident's card to chat at their home, or choose Visit in person to meet them in the settlement.",
      PartyMembers: empty ? [] : people.map(([Name, DisplayStatus, asset], i) => ({
        ...portraitFields(`resident_${i}`, asset), Name, DisplayStatus, StatusTop: 56, StatusHeight: 21,
        IsActive: atHome && i === 0, IsInactive: !atHome || i !== 0, CanVisitInPerson: true
      })),
      ChatLines: atHome ? [
        { Speaker: "Livia Bellori", Text: '"Come in. The kettle is warm, and there is a seat by the window."', IsNpcLine: true, IsPlayerLine: false, IsSystemLine: false },
        { Speaker: "You", Text: "How have things been in Zeonica?", IsNpcLine: false, IsPlayerLine: true, IsSystemLine: false }
      ] : [],
      $emptyListRationales: { PartyMembers: "No residents have been met in this settlement yet.", ChatLines: "The Homes directory starts no conversation; selecting one resident opens their private session." }
    });
  }
  if (preset === "intoxication-actions") result.ChatLines = [
    { Speaker: "You", Text: "Perhaps you should rest.", Role: "user", IsPlayerLine: true, IsNpcLine: false, IsSystemLine: false },
    ...[
      '*They take a sip of wine, smiling faintly.* "A pleasant evening."',
      '"I am perfectly steady." *They reach for the table and miss, then laugh at themselves.*',
      '*Their eyes drift shut. Too sick to sit upright, they stir and mumble.* "No more..."'
    ].map((Text) => ({ Speaker: "Gwydan", Text, Role: "assistant", IsNpcLine: true, IsPlayerLine: false, IsSystemLine: false }))
  ];
  if (Array.isArray(result.ChatLines)) result.ChatLines = result.ChatLines.map((line) => ({
    ...line, RichText: previewActionRichText(line.Text, line.IsNpcLine || line.Role === "npc" || line.Role === "assistant")
  }));
  return result;
}

export function getPreviewFixtureInventory() {
  return Object.keys(PREFAB_ROOT_KEYS).sort().map((fileName) => ({
    fileName,
    rootKeys: [...PREFAB_ROOT_KEYS[fileName]],
    hasOverride: Object.prototype.hasOwnProperty.call(PREFAB_SAMPLE_OVERRIDES, fileName)
  }));
}

export function applyCourtState(state) {
  SAMPLE_DATA.HomeVisible = state === "home";
  SAMPLE_DATA.LargeHomeVisible = SAMPLE_DATA.HomeVisible && SAMPLE_DATA.UseLargeCourtLayout;
  SAMPLE_DATA.ReferenceHomeVisible = SAMPLE_DATA.HomeVisible && !SAMPLE_DATA.UseLargeCourtLayout;
  SAMPLE_DATA.TabContentVisible = state !== "home";
  SAMPLE_DATA.DetailVisible = state === "detail" || state === "audience";
  SAMPLE_DATA.AudienceVisible = state === "audience";
  SAMPLE_DATA.ActionsVisible = state === "detail";
}
